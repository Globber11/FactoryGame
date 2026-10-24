using System.Collections.Generic;

namespace Factory.Core
{
    // Бесконечная сетка из чанков 16x16.
    // Чанки лежат в списке, отсортированном по (Cy, Cx): порядок обхода не зависит от порядка постройки.
    public sealed class Layout
    {
        public readonly int Id;
        public long Tick;
        public int Delivered;

        // Слой, в котором стоит клетка-модуль, владеющая этим слоем (-1 у корня)
        public int ParentId { get; internal set; }
        public int ParentX { get; internal set; }
        public int ParentY { get; internal set; }

        // Зерно генерации зон слоя: зоны вычисляются из него и координат, в клетках не хранятся
        public int Seed { get; internal set; }

        // Слой заморожен, если выключен модуль, которому он принадлежит, или любой модуль выше (считает World.Step)
        public bool Frozen { get; internal set; }

        public int OutputCount { get; private set; }

        // Сколько выходов смотрит в каждую сторону (индекс = Dir). По ним снаружи видно, откуда модуль выдаёт предметы
        readonly int[] _outputDirs = new int[4];
        public bool HasOutputFacing(Dir d) { return _outputDirs[(int)d] > 0; }
        public int InputCount { get { return _inputs.Count; } }

        public int NodeCount => _nodes.Count;
        public int ProducerCount => _producers.Count;
        public int ConsumerCount => _consumers.Count;

        public bool CompressedAllowed => ParentId >= 0;   // сжатое пространство бывает только внутри модулей
        public ZoneKind ZoneAt(int x, int y) => ZoneGenerator.ZoneAt(Seed, CompressedAllowed, x, y);

        // Расход энергии модуля, внутри которого лежит этот слой (единиц за такт)
        public int EnergyDemand
        {
            get
            {
                if (_demand < 0) _demand = Energy.Demand(Energy.Complexity(this));
                return _demand;
            }
        }

        readonly Dictionary<long, Chunk> _map = new Dictionary<long, Chunk>();
        readonly List<Chunk> _chunks = new List<Chunk>();
        readonly List<long> _inputs = new List<long>();   // позиции входов, отсортированы
        readonly List<long> _nodes = new List<long>();        // квантовые узлы
        readonly List<long> _producers = new List<long>();    // флуктуаторы (производят энергию)
        readonly List<long> _consumers = new List<long>();    // потребители энергии: модули и экстракторы
        int _demand = -1;                                     // кэш расхода энергии, сбрасывается при любой правке слоя

        // Рабочие массивы энергосети (чтобы не создавать мусор каждый такт)
        internal int[] Scratch1, Scratch2;

        // Габариты содержимого (кэш, сбрасывается при любой правке слоя)
        bool _boundsValid;
        bool _boundsAny;
        int _bMinX, _bMinY, _bMaxX, _bMaxY;

        public Layout(int id)
        {
            Id = id;
            ParentId = -1;
        }

        public IReadOnlyList<Chunk> Chunks => _chunks;
        public int ChunkCount => _chunks.Count;

        static long Key(int cx, int cy) => ((long)cy << 32) | (uint)cx;
        static long PosKey(int x, int y) => ((long)y << 32) | (uint)x;

        // x >> 4 даёт floor-деление, поэтому отрицательные координаты работают корректно
        internal bool TryLocate(int x, int y, out Chunk chunk, out int index)
        {
            if (_map.TryGetValue(Key(x >> Chunk.Shift, y >> Chunk.Shift), out chunk))
            {
                index = ((y & Chunk.Mask) << Chunk.Shift) | (x & Chunk.Mask);
                return true;
            }
            index = 0;
            return false;
        }

        // n-й вход в стабильном порядке (по y, затем по x)
        internal void InputAt(int n, out int x, out int y)
        {
            long k = _inputs[n];
            unchecked
            {
                x = (int)(k & 0xFFFFFFFFL);
                y = (int)(k >> 32);
            }
        }

        // Минимальный прямоугольник, охватывающий все непустые клетки; false, если слой пуст
        public bool GetBounds(out int minX, out int minY, out int maxX, out int maxY)
        {
            if (!_boundsValid) RecomputeBounds();
            minX = _bMinX; minY = _bMinY; maxX = _bMaxX; maxY = _bMaxY;
            return _boundsAny;
        }

        void RecomputeBounds()
        {
            _boundsValid = true;
            _boundsAny = false;
            _bMinX = _bMinY = _bMaxX = _bMaxY = 0;

            for (int n = 0; n < _chunks.Count; n++)
            {
                Chunk ch = _chunks[n];
                int baseX = ch.Cx << Chunk.Shift;
                int baseY = ch.Cy << Chunk.Shift;

                for (int i = 0; i < Chunk.CellCount; i++)
                {
                    if (ch.Cells[i].Kind == CellKind.Empty) continue;

                    int x = baseX + (i & Chunk.Mask);
                    int y = baseY + (i >> Chunk.Shift);

                    if (!_boundsAny) { _bMinX = _bMaxX = x; _bMinY = _bMaxY = y; _boundsAny = true; }
                    else
                    {
                        if (x < _bMinX) _bMinX = x;
                        if (x > _bMaxX) _bMaxX = x;
                        if (y < _bMinY) _bMinY = y;
                        if (y > _bMaxY) _bMaxY = y;
                    }
                }
            }
        }

        static void Unpack(long k, out int x, out int y)
        {
            unchecked
            {
                x = (int)(k & 0xFFFFFFFFL);
                y = (int)(k >> 32);
            }
        }

        // Позиции узлов, флуктуаторов и потребителей в стабильном порядке (по y, затем по x)
        public void NodeAt(int n, out int x, out int y) { Unpack(_nodes[n], out x, out y); }
        public void ProducerAt(int n, out int x, out int y) { Unpack(_producers[n], out x, out y); }
        public void ConsumerAt(int n, out int x, out int y) { Unpack(_consumers[n], out x, out y); }

        public Cell Get(int x, int y)
        {
            Chunk c; int i;
            return TryLocate(x, y, out c, out i) ? c.Cells[i] : default(Cell);
        }

        public void Set(int x, int y, CellKind kind, Dir facing, int link = 0)
        {
            Set(x, y, new Cell { Kind = kind, Facing = facing, Link = link });
        }

        public void Set(int x, int y, Cell cell)
        {
            _demand = -1;   // сложность слоя изменилась
            _boundsValid = false;
            int cx = x >> Chunk.Shift, cy = y >> Chunk.Shift;
            Chunk chunk;
            if (!_map.TryGetValue(Key(cx, cy), out chunk))
            {
                if (cell.Kind == CellKind.Empty) return;
                chunk = AddChunk(cx, cy);
            }

            int i = ((y & Chunk.Mask) << Chunk.Shift) | (x & Chunk.Mask);
            CellKind oldKind = chunk.Cells[i].Kind;
            Dir oldFacing = chunk.Cells[i].Facing;
            bool wasEmpty = oldKind == CellKind.Empty;
            bool isEmpty = cell.Kind == CellKind.Empty;

            chunk.Cells[i] = cell;
            if (wasEmpty && !isEmpty) chunk.Occupied++;
            else if (!wasEmpty && isEmpty) chunk.Occupied--;

            if (oldKind == CellKind.Input) RemoveInput(x, y);
            if (cell.Kind == CellKind.Input) AddInput(x, y);
            if (oldKind == CellKind.Output) { OutputCount--; _outputDirs[(int)oldFacing]--; }
            if (cell.Kind == CellKind.Output) { OutputCount++; _outputDirs[(int)cell.Facing]++; }

            if (IsConsumer(oldKind)) ListRemove(_consumers, x, y);
            if (IsConsumer(cell.Kind)) ListAdd(_consumers, x, y);
            if (oldKind == CellKind.Fluctuator) ListRemove(_producers, x, y);
            if (cell.Kind == CellKind.Fluctuator) ListAdd(_producers, x, y);
            if (oldKind == CellKind.QuantumNode) ListRemove(_nodes, x, y);
            if (cell.Kind == CellKind.QuantumNode) ListAdd(_nodes, x, y);

            if (chunk.Occupied == 0) RemoveChunk(chunk);
        }

        void AddInput(int x, int y)
        {
            long k = PosKey(x, y);
            int idx = _inputs.BinarySearch(k);
            if (idx < 0) _inputs.Insert(~idx, k);
        }

        void RemoveInput(int x, int y)
        {
            int idx = _inputs.BinarySearch(PosKey(x, y));
            if (idx >= 0) _inputs.RemoveAt(idx);
        }

        static bool IsConsumer(CellKind k) => k == CellKind.Module || k == CellKind.Extractor;

        static void ListAdd(List<long> list, int x, int y)
        {
            long k = PosKey(x, y);
            int idx = list.BinarySearch(k);
            if (idx < 0) list.Insert(~idx, k);
        }

        static void ListRemove(List<long> list, int x, int y)
        {
            int idx = list.BinarySearch(PosKey(x, y));
            if (idx >= 0) list.RemoveAt(idx);
        }

        Chunk AddChunk(int cx, int cy)
        {
            var chunk = new Chunk(cx, cy);
            int lo = 0, hi = _chunks.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                Chunk m = _chunks[mid];
                if (m.Cy < cy || (m.Cy == cy && m.Cx < cx)) lo = mid + 1; else hi = mid;
            }
            _chunks.Insert(lo, chunk);
            _map[Key(cx, cy)] = chunk;
            return chunk;
        }

        void RemoveChunk(Chunk chunk)
        {
            _chunks.Remove(chunk);
            _map.Remove(Key(chunk.Cx, chunk.Cy));
        }

        internal static ulong Mix(ulong h, ulong v)
        {
            unchecked { return (h ^ v) * 1099511628211UL; }
        }

        public ulong Hash()
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int n = 0; n < _chunks.Count; n++)
                {
                    Chunk ch = _chunks[n];
                    h = Mix(h, (ulong)(uint)ch.Cx);
                    h = Mix(h, (ulong)(uint)ch.Cy);
                    for (int i = 0; i < Chunk.CellCount; i++)
                    {
                        Cell c = ch.Cells[i];
                        h = Mix(h, (ulong)c.Kind);
                        h = Mix(h, (ulong)c.Facing);
                        h = Mix(h, c.Item);
                        h = Mix(h, (ulong)(uint)c.Timer);
                        h = Mix(h, (ulong)(uint)c.Link);
                        h = Mix(h, c.Powered);
                    }
                }
                h = Mix(h, (ulong)Tick);
                h = Mix(h, (ulong)(uint)Delivered);
                h = Mix(h, (ulong)(uint)Seed);
                return h;
            }
        }
    }
}