namespace Factory.Core
{
    public struct CostInfo
    {
        public int Blocks;   // сумма цен блоков этого слоя
        public int Size;     // надбавка за прямоугольник, в который помещается содержимое
        public int Nested;   // полная цена вложенных модулей
        public int Width, Height;

        public int Total => Blocks + Size + Nested;
    }

    // Цена постановки одного блока в низшей эссенции
    public struct PlaceCostInfo
    {
        public int Block;   // цена самого блока
        public int Size;    // рост надбавки за размер прямоугольника, в который помещается слой
        public int Total => Block + Size;
    }

    public static class Cost
    {
        // Временные значения для настройки баланса
        public const int Belt = 1;
        public const int Input = 2;
        public const int Sink = 3;
        public const int Source = 4;
        public const int Output = 10;
        public const int Extractor = 8;
        public const int Stabilizer = 12;
        public const int Fluctuator = 8;
        public const int QuantumNode = 6;
        public const int Module = 15;   // сама клетка-модуль; содержимое оплачивается блоками внутри
        public const int SizeDivisor = 2;   // надбавка = площадь прямоугольника / SizeDivisor

        public static int BlockCost(CellKind k)
        {
            switch (k)
            {
                case CellKind.Belt: return Belt;
                case CellKind.Input: return Input;
                case CellKind.Sink: return Sink;
                case CellKind.Source: return Source;
                case CellKind.Output: return Output;
                case CellKind.Extractor: return Extractor;
                case CellKind.Stabilizer: return Stabilizer;
                case CellKind.Fluctuator: return Fluctuator;
                case CellKind.QuantumNode: return QuantumNode;
                case CellKind.Module: return Module;
                default: return 0;   // пустые клетки бесплатны
            }
        }

        static int SizeSurcharge(int width, int height)
        {
            long a = (long)width * height / SizeDivisor;
            return a > 1000000000L ? 1000000000 : (int)a;
        }

        // Цена постановки блока (x, y) игроком: цена блока плюс рост надбавки за размер прямоугольника слоя.
        // Поворот уже стоящего блока того же вида бесплатен.
        public static PlaceCostInfo PlaceCost(Layout l, int x, int y, CellKind kind)
        {
            var info = new PlaceCostInfo();
            if (kind == CellKind.Empty) return info;
            if (l.Get(x, y).Kind == kind) return info;

            info.Block = BlockCost(kind);

            int minX, minY, maxX, maxY;
            int before = 0;
            if (l.GetBounds(out minX, out minY, out maxX, out maxY))
            {
                before = SizeSurcharge(maxX - minX + 1, maxY - minY + 1);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            else
            {
                minX = maxX = x;
                minY = maxY = y;
            }

            info.Size = SizeSurcharge(maxX - minX + 1, maxY - minY + 1) - before;
            return info;
        }

        public static CostInfo Of(World w, Layout l)
        {
            var info = new CostInfo();
            bool any = false;
            int minX = 0, maxX = 0, minY = 0, maxY = 0;

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

                    if (!any) { minX = maxX = x; minY = maxY = y; any = true; }
                    else
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }

                    if (c.Kind == CellKind.Module)
                    {
                        info.Blocks += Module;
                        Layout child = w.Get(c.Link);
                        if (child != null) info.Nested += Of(w, child).Total;
                    }
                    else
                    {
                        info.Blocks += BlockCost(c.Kind);
                    }
                }
            }

            if (any)
            {
                info.Width = maxX - minX + 1;
                info.Height = maxY - minY + 1;
                info.Size = info.Width * info.Height / SizeDivisor;
            }
            return info;
        }
    }
}