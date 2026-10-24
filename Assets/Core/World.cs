using System.Collections.Generic;

namespace Factory.Core
{
    public enum PlaceResult { Ok, OutputLimit, NotInRoot, WrongZone, NotEnoughEssence }

    // Хранит все слои. id слоя = индекс в списке, удалённый слой = null (id не переиспользуются).
    public sealed class World
    {
        readonly List<Layout> _layouts = new List<Layout>();

        public long Tick { get; private set; }

        // Сколько выходов можно поставить в каждом модуле (позже будет расти с прокачкой)
        public int MaxOutputs { get; set; } = 1;

        // Зерно мира: из него и id слоя получается seed зон каждого слоя
        public int Seed { get; }

        // Общий запас эссенций: пополняют приёмники, низшая тратится на строительство (Build)
        public const long StartLower = 300;
        public long Higher { get; private set; }
        public long Lower { get; private set; }

        public World(int seed = 1)
        {
            Seed = seed;
            Lower = StartLower;
            CreateLayout(-1, 0, 0);
        }

        public Layout Root => _layouts[0];

        public Layout Get(int id) => id >= 0 && id < _layouts.Count ? _layouts[id] : null;

        public int LayoutCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _layouts.Count; i++) if (_layouts[i] != null) n++;
                return n;
            }
        }

        Layout CreateLayout(int parentId, int px, int py)
        {
            var l = new Layout(_layouts.Count);
            l.ParentId = parentId;
            l.Seed = ZoneGenerator.SeedFor(Seed, l.Id);
            l.ParentX = px;
            l.ParentY = py;
            _layouts.Add(l);
            return l;
        }

        // Все правки идут через мир: он следит за лимитами, создаёт слой при постановке модуля
        // и удаляет его вместе с модулем
        // Проверка правил постановки (мир не меняется)
        public PlaceResult Validate(Layout layout, int x, int y, CellKind kind)
        {
            bool isPort = kind == CellKind.Input || kind == CellKind.Output;
            if (isPort && layout.ParentId < 0) return PlaceResult.NotInRoot;

            Cell old = layout.Get(x, y);
            if (kind == CellKind.Output && old.Kind != CellKind.Output && layout.OutputCount >= MaxOutputs)
                return PlaceResult.OutputLimit;

            // Экстрактор только на области высокой плотности, флуктуатор только на дестабилизированной
            if (kind == CellKind.Extractor && layout.ZoneAt(x, y) != ZoneKind.HighDensity)
                return PlaceResult.WrongZone;
            if (kind == CellKind.Fluctuator && layout.ZoneAt(x, y) != ZoneKind.Destabilized)
                return PlaceResult.WrongZone;

            return PlaceResult.Ok;
        }

        // БЕСПЛАТНАЯ постановка (демо-мир, внутренние правки, стирание). Игрок строит через Build.
        public PlaceResult Place(Layout layout, int x, int y, CellKind kind, Dir facing)
        {
            PlaceResult v = Validate(layout, x, y, kind);
            if (v != PlaceResult.Ok) return v;

            Cell old = layout.Get(x, y);

            // Поворот стабилизатора не сбрасывает его режим
            if (kind == CellKind.Stabilizer && old.Kind == CellKind.Stabilizer)
            {
                old.Facing = facing;
                layout.Set(x, y, old);
                return PlaceResult.Ok;
            }

            if (old.Kind == CellKind.Module) DestroyTree(old.Link);

            if (kind == CellKind.Empty || kind == CellKind.Module
                || kind == CellKind.Fluctuator || kind == CellKind.QuantumNode) facing = Dir.N;
            int link = 0;
            if (kind == CellKind.Module) link = CreateLayout(layout.Id, x, y).Id;

            layout.Set(x, y, kind, facing, link);
            return PlaceResult.Ok;
        }

        public void Erase(Layout layout, int x, int y)
        {
            Place(layout, x, y, CellKind.Empty, Dir.N);
        }

        // Постановка игроком: проверка правил, оплата низшей эссенцией (Cost.PlaceCost), затем сама постановка.
        // Поворот уже стоящего блока того же вида бесплатен. Сноса возврата нет.
        public PlaceResult Build(Layout layout, int x, int y, CellKind kind, Dir facing)
        {
            PlaceResult v = Validate(layout, x, y, kind);
            if (v != PlaceResult.Ok) return v;

            int cost = Cost.PlaceCost(layout, x, y, kind).Total;
            if (cost > Lower) return PlaceResult.NotEnoughEssence;

            PlaceResult r = Place(layout, x, y, kind, facing);
            if (r == PlaceResult.Ok) Lower -= cost;
            return r;
        }

        // Приёмник собирает высшую и низшую эссенции в общий запас; остальные предметы просто исчезают
        internal void Collect(byte item)
        {
            if (item == Items.HigherEssence) Higher++;
            else if (item == Items.LowerEssence) Lower++;
        }

        // Отладочное пополнение запаса
        public void AddEssence(long higher, long lower)
        {
            Higher += higher;
            Lower += lower;
        }

        // Переключает режим стабилизатора по кругу; возвращает новый режим или -1, если там нет стабилизатора
        public int CycleStabilizerMode(Layout layout, int x, int y)
        {
            Cell c = layout.Get(x, y);
            if (c.Kind != CellKind.Stabilizer) return -1;

            c.Timer = (c.Timer + 1) % StabilizerMode.Count;
            layout.Set(x, y, c);
            return c.Timer;
        }

        void DestroyTree(int id)
        {
            Layout l = Get(id);
            if (l == null) return;
            _layouts[id] = null;

            var chunks = l.Chunks;
            for (int n = 0; n < chunks.Count; n++)
            {
                Cell[] cells = chunks[n].Cells;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i].Kind == CellKind.Module) DestroyTree(cells[i].Link);
            }
        }

        // Слой заморожен, если выключен модуль, которому он принадлежит, или любой модуль выше.
        // Родительский слой всегда создаётся раньше дочернего (меньший id), поэтому хватает одного прохода по id.
        bool IsFrozen(Layout l)
        {
            if (l.ParentId < 0) return false;
            Layout p = Get(l.ParentId);
            if (p == null) return true;

            Cell m = p.Get(l.ParentX, l.ParentY);
            return p.Frozen || m.Kind != CellKind.Module || m.Powered == 0;
        }

        // Такт: 1) питание, 2) три фазы симуляции (предметы переходят между слоями).
        // Замороженные слои (модули без питания) не симулируются.
        public void Step()
        {
            for (int i = 0; i < _layouts.Count; i++)
            {
                Layout l = _layouts[i];
                if (l == null) continue;

                l.Frozen = IsFrozen(l);
                if (!l.Frozen) EnergyNetwork.Update(this, l);
            }

            for (int i = 0; i < _layouts.Count; i++)
                if (_layouts[i] != null && !_layouts[i].Frozen) Simulation.Begin(_layouts[i]);

            for (int i = 0; i < _layouts.Count; i++)
                if (_layouts[i] != null && !_layouts[i].Frozen) Simulation.Process(this, _layouts[i]);

            for (int i = 0; i < _layouts.Count; i++)
                if (_layouts[i] != null && !_layouts[i].Frozen) Simulation.End(_layouts[i]);

            Tick++;
        }

        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < _layouts.Count; i++)
            {
                if (_layouts[i] == null) continue;
                h = Layout.Mix(h, (ulong)(uint)i);
                h = Layout.Mix(h, _layouts[i].Hash());
            }
            h = Layout.Mix(h, (ulong)Higher);
            h = Layout.Mix(h, (ulong)Lower);
            return Layout.Mix(h, (ulong)Tick);
        }
    }
}