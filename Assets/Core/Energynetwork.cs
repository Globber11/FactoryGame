namespace Factory.Core
{
    // Энергосеть слоя. Квантовый узел связывает в радиусе флуктуаторы (производят энергию)
    // и потребителей (модули, экстракторы). Узлы, которым достаётся общий элемент, образуют одну сеть.
    // Расчёт идёт каждый такт ПЕРЕД симуляцией и выставляет Cell.Powered у потребителей.
    //
    // Модель без буфера: выработка сети за такт делится между потребителями в стабильном порядке
    // (по позиции: сначала меньший y, затем меньший x). Кому не хватило, остаётся без питания.
    public static class EnergyNetwork
    {
        // Целочисленное расстояние (квадрат Евклида), без float
        public static bool InRange(int ax, int ay, int bx, int by)
        {
            int dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= Energy.NodeRadius * Energy.NodeRadius;
        }

        internal static void Update(World w, Layout l)
        {
            int nc = l.ConsumerCount;
            if (nc == 0) return;

            int nn = l.NodeCount;
            int px, py;

            if (nn == 0)
            {
                for (int k = 0; k < nc; k++)
                {
                    l.ConsumerAt(k, out px, out py);
                    SetPowered(l, px, py, 0);
                }
                return;
            }

            if (l.Scratch1 == null || l.Scratch1.Length < nn)
            {
                l.Scratch1 = new int[nn * 2];
                l.Scratch2 = new int[nn * 2];
            }
            int[] uf = l.Scratch1;       // система непересекающихся множеств по узлам
            int[] budget = l.Scratch2;   // выработка сети (по корню множества)
            for (int i = 0; i < nn; i++) { uf[i] = i; budget[i] = 0; }

            int np = l.ProducerCount;

            // 1) Объединяем узлы, в радиусе которых оказался один и тот же элемент
            for (int k = 0; k < np; k++) { l.ProducerAt(k, out px, out py); Link(l, uf, nn, px, py); }
            for (int k = 0; k < nc; k++) { l.ConsumerAt(k, out px, out py); Link(l, uf, nn, px, py); }

            // 2) Выработка
            for (int k = 0; k < np; k++)
            {
                l.ProducerAt(k, out px, out py);
                int n = FirstNode(l, nn, px, py);
                if (n >= 0) budget[Find(uf, n)] += Energy.FluctuatorOutput;
            }

            // 3) Потребители в стабильном порядке
            for (int k = 0; k < nc; k++)
            {
                l.ConsumerAt(k, out px, out py);
                byte on = 0;
                int n = FirstNode(l, nn, px, py);
                if (n >= 0)
                {
                    int root = Find(uf, n);
                    int demand = DemandAt(w, l, px, py);
                    if (budget[root] >= demand)
                    {
                        budget[root] -= demand;
                        on = 1;
                    }
                }
                SetPowered(l, px, py, on);
            }
        }

        static int DemandAt(World w, Layout l, int x, int y)
        {
            Cell c = l.Get(x, y);
            if (c.Kind == CellKind.Extractor) return Energy.ExtractorDemand;
            if (c.Kind == CellKind.Module)
            {
                Layout child = w.Get(c.Link);
                return child == null ? 0 : child.EnergyDemand;
            }
            return 0;
        }

        // Первый (по порядку) узел, в радиусе которого лежит точка, или -1
        static int FirstNode(Layout l, int nn, int x, int y)
        {
            for (int n = 0; n < nn; n++)
            {
                int nx, ny;
                l.NodeAt(n, out nx, out ny);
                if (InRange(nx, ny, x, y)) return n;
            }
            return -1;
        }

        // Объединяет все узлы, в радиусе которых лежит точка
        static void Link(Layout l, int[] uf, int nn, int x, int y)
        {
            int first = -1;
            for (int n = 0; n < nn; n++)
            {
                int nx, ny;
                l.NodeAt(n, out nx, out ny);
                if (!InRange(nx, ny, x, y)) continue;
                if (first < 0) first = n;
                else Union(uf, first, n);
            }
        }

        static int Find(int[] uf, int a)
        {
            while (uf[a] != a)
            {
                uf[a] = uf[uf[a]];
                a = uf[a];
            }
            return a;
        }

        static void Union(int[] uf, int a, int b)
        {
            int ra = Find(uf, a), rb = Find(uf, b);
            if (ra == rb) return;
            // Корнем всегда становится меньший индекс: результат не зависит от порядка объединения
            if (ra < rb) uf[rb] = ra; else uf[ra] = rb;
        }

        static void SetPowered(Layout l, int x, int y, byte on)
        {
            Chunk ch; int i;
            if (l.TryLocate(x, y, out ch, out i)) ch.Cells[i].Powered = on;
        }
    }
}