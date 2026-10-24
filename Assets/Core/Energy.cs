namespace Factory.Core
{
    // Квантовая энергия - не предмет, а отдельная система (см. EnergyNetwork).
    // Здесь константы баланса и формулы расхода.
    public static class Energy
    {
        public const int FluctuatorOutput = 120;   // выработка одного флуктуатора, единиц за такт
        public const int ExtractorDemand = 20;     // расход одного экстрактора, единиц за такт
        public const int NodeRadius = 12;          // радиус действия квантового узла, клеток
        public const int ModuleWeight = 5;         // вес вложенного модуля в сложности слоя (его содержимое не считается)
        public const int MaxDemand = 10000000;     // верхняя граница расхода, чтобы не было переполнения int

        // Сложность слоя = блоки + надбавка за размер + вложенные модули по ModuleWeight.
        // Содержимое вложенных модулей НЕ входит: они платят за себя сами. Поэтому дробление
        // большой фабрики на вложенные модули снижает суммарный расход.
        public static int Complexity(Layout l)
        {
            int sum = 0;
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
                    CellKind k = ch.Cells[i].Kind;
                    if (k == CellKind.Empty) continue;

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

                    sum += k == CellKind.Module ? ModuleWeight : Cost.BlockCost(k);
                }
            }

            if (any) sum += (maxX - minX + 1) * (maxY - minY + 1) / Cost.SizeDivisor;
            return sum;
        }

        // Расход энергии модуля за такт: сложность в степени 1.25 (сверхлинейно), только целые числа.
        // C^1.25 = C * (C * 10^8)^(1/4) / 100
        public static int Demand(int complexity)
        {
            if (complexity <= 0) return 0;
            long root4 = ISqrt(ISqrt((long)complexity * 100000000L));
            long r = (long)complexity * root4 / 100;
            return r > MaxDemand ? MaxDemand : (int)r;
        }

        // Целочисленный квадратный корень (округление вниз), без float
        static long ISqrt(long n)
        {
            if (n <= 0) return 0;
            long x = n;
            long y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }
            return x;
        }
    }
}