using System;

namespace Factory.Core
{
    public static class Simulation
    {
        public const int SourcePeriod = 4;
        public const int ExtractorPeriod = 6;   // экстрактор выдаёт эссенцию раз в столько тактов
        public const int BeltPeriod = 4;        // предмет проводит в клетке ленты столько тактов, потом уходит вперёд

        // Фаза 1: копируем текущее состояние в рабочий буфер
        internal static void Begin(Layout l)
        {
            var chunks = l.Chunks;
            for (int n = 0; n < chunks.Count; n++)
                Array.Copy(chunks[n].Cells, chunks[n].Next, Chunk.CellCount);
        }

        // Фаза 2: решения принимаются по Cells (состояние на начало такта), результат пишется в Next.
        // Предмет может уйти в другой слой, поэтому все слои обрабатываются до обмена буферов.
        internal static void Process(World w, Layout l)
        {
            var chunks = l.Chunks;
            for (int n = 0; n < chunks.Count; n++)
            {
                Chunk ch = chunks[n];
                int baseX = ch.Cx << Chunk.Shift;
                int baseY = ch.Cy << Chunk.Shift;

                for (int i = 0; i < Chunk.CellCount; i++)
                {
                    Cell c = ch.Cells[i];
                    if (c.Kind == CellKind.Empty) continue;

                    int x = baseX + (i & Chunk.Mask);
                    int y = baseY + (i >> Chunk.Shift);
                    int tx = x + DirUtil.Dx(c.Facing);
                    int ty = y + DirUtil.Dy(c.Facing);

                    if (c.Kind == CellKind.Belt || c.Kind == CellKind.Input)
                    {
                        if (c.Item == 0) continue;

                        // Timer = сколько тактов предмет уже в этой клетке. Вход ведёт себя как лента.
                        int timer = c.Timer + 1;
                        if (timer > BeltPeriod) timer = BeltPeriod;

                        if (timer >= BeltPeriod && TryDeliver(w, l, tx, ty, c.Facing, c.Item))
                        {
                            ch.Next[i].Item = 0;
                            ch.Next[i].Timer = 0;
                        }
                        else
                        {
                            ch.Next[i].Timer = timer;
                        }
                    }
                    else if (c.Kind == CellKind.Output)
                    {
                        if (c.Item == 0 || l.ParentId < 0) continue;
                        Layout parent = w.Get(l.ParentId);
                        if (parent == null) continue;

                        // Выход отдаёт предмет в клетку рядом с модулем, с той стороны, куда смотрит стрелка
                        int px = l.ParentX + DirUtil.Dx(c.Facing);
                        int py = l.ParentY + DirUtil.Dy(c.Facing);
                        if (TryDeliver(w, parent, px, py, c.Facing, c.Item))
                            ch.Next[i].Item = 0;
                    }
                    else if (c.Kind == CellKind.Source)
                    {
                        int timer = c.Timer + 1;
                        if (timer > SourcePeriod) timer = SourcePeriod;
                        if (timer >= SourcePeriod && TryDeliver(w, l, tx, ty, c.Facing, Items.Basic))
                            timer = 0;
                        ch.Next[i].Timer = timer;
                    }
                    else if (c.Kind == CellKind.Extractor)
                    {
                        // Без питания не работает, таймер стоит. На область высокой плотности его ставит World.Place
                        if (c.Powered == 0) continue;

                        int timer = c.Timer + 1;
                        if (timer > ExtractorPeriod) timer = ExtractorPeriod;
                        if (timer >= ExtractorPeriod && TryDeliver(w, l, tx, ty, c.Facing, Items.UnstableEssence))
                            timer = 0;
                        ch.Next[i].Timer = timer;
                    }
                    else if (c.Kind == CellKind.Stabilizer)
                    {
                        UpdateStabilizer(w, l, ch, i, c, tx, ty);
                    }
                    // Fluctuator, QuantumNode и питание модулей: обрабатывает EnergyNetwork перед тактом
                }
            }
        }

        // Стабилизатор держит один предмет: принял нестабильную эссенцию, за такт превратил её в продукт
        // и затем отдаёт вперёд. Режим в Timer, в режиме "оба" виды чередуются (указатель в Link).
        static void UpdateStabilizer(World w, Layout l, Chunk ch, int i, Cell c, int tx, int ty)
        {
            if (c.Item == Items.UnstableEssence)
            {
                byte product;
                int link = c.Link;

                if (c.Timer == StabilizerMode.Both)
                {
                    product = link == 0 ? Items.HigherEssence : Items.LowerEssence;
                    link = 1 - link;
                }
                else
                {
                    product = c.Timer == StabilizerMode.Lower ? Items.LowerEssence : Items.HigherEssence;
                }

                ch.Next[i].Item = product;
                ch.Next[i].Link = link;
            }
            else if (c.Item != 0)
            {
                if (TryDeliver(w, l, tx, ty, c.Facing, c.Item)) ch.Next[i].Item = 0;
            }
        }

        // Фаза 3: обмен буферов
        internal static void End(Layout l)
        {
            var chunks = l.Chunks;
            for (int n = 0; n < chunks.Count; n++)
            {
                Chunk ch = chunks[n];
                Cell[] tmp = ch.Cells;
                ch.Cells = ch.Next;
                ch.Next = tmp;
            }
            l.Tick++;
        }

        // Пытается передать предмет в клетку (x, y) слоя dst. moveDir - направление движения предмета.
        static bool TryDeliver(World w, Layout dst, int x, int y, Dir moveDir, byte item)
        {
            Chunk tc; int ti;
            if (!dst.TryLocate(x, y, out tc, out ti)) return false;
            Cell t = tc.Cells[ti];

            switch (t.Kind)
            {
                case CellKind.Sink:
                    dst.Delivered++;
                    w.Collect(item);   // высшая и низшая эссенции пополняют общий запас
                    return true;

                case CellKind.Belt:
                    // Лента не принимает предмет "лоб в лоб"
                    if (t.Facing == DirUtil.Opposite(moveDir)) return false;
                    if (t.Item != 0 || tc.Next[ti].Item != 0) return false;
                    tc.Next[ti].Item = item;
                    tc.Next[ti].Timer = 0;   // новый предмет начинает путь по клетке
                    return true;

                case CellKind.Output:
                    if (t.Item != 0 || tc.Next[ti].Item != 0) return false;
                    tc.Next[ti].Item = item;
                    return true;

                case CellKind.Stabilizer:
                    // Принимает только нестабильную эссенцию и не принимает "лоб в лоб"
                    if (item != Items.UnstableEssence) return false;
                    if (t.Facing == DirUtil.Opposite(moveDir)) return false;
                    if (t.Item != 0 || tc.Next[ti].Item != 0) return false;
                    tc.Next[ti].Item = item;
                    return true;

                case CellKind.Module:
                    return TryAcceptModule(w, tc, ti, item);

                default:
                    return false;
            }
        }

        // Модуль раздаёт входящие предметы по своим входам по кругу, занятые входы пропускает
        static bool TryAcceptModule(World w, Chunk mc, int mi, byte item)
        {
            if (mc.Cells[mi].Powered == 0) return false;   // модуль без питания ничего не принимает

            Layout child = w.Get(mc.Cells[mi].Link);
            if (child == null) return false;

            int n = child.InputCount;
            if (n == 0) return false;

            int start = mc.Next[mi].Timer % n;
            for (int k = 0; k < n; k++)
            {
                int idx = (start + k) % n;
                int ix, iy;
                child.InputAt(idx, out ix, out iy);

                Chunk ic; int ii;
                if (!child.TryLocate(ix, iy, out ic, out ii)) continue;
                if (ic.Cells[ii].Item != 0 || ic.Next[ii].Item != 0) continue;

                ic.Next[ii].Item = item;
                ic.Next[ii].Timer = 0;
                mc.Next[mi].Timer = (idx + 1) % n;
                return true;
            }
            return false;
        }
    }
}