namespace Factory.Core
{
    // Зоны не хранятся в клетках: слой бесконечный, а чанки без построек удаляются.
    // Зона клетки вычисляется чистой функцией от seed слоя и координат, поэтому она не меняется,
    // не занимает памяти и не требует записи в сохранение (достаточно seed).
    //
    // Каждый чанк 16x16 содержит не более одного круглого пятна: центр в [5..10] от угла чанка,
    // радиус 3..5. Пятно всегда целиком лежит в своём чанке, для проверки клетки хватает одного чанка.
    public static class ZoneGenerator
    {
        public static int SeedFor(int worldSeed, int layoutId)
        {
            unchecked { return (int)Mix64(((ulong)(uint)worldSeed << 32) | (uint)layoutId); }
        }

        static ulong Mix64(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        static ulong Roll(int seed, int cx, int cy)
        {
            unchecked
            {
                ulong h = Mix64((ulong)(uint)seed);
                h = Mix64(h ^ (ulong)(uint)cx);
                h = Mix64(h ^ ((ulong)(uint)cy << 32));
                return h;
            }
        }

        // Пятно в чанке (cx, cy): вид зоны, центр (px, py) в координатах слоя и радиус r.
        // compressedAllowed: сжатое пространство бывает только внутри модулей.
        public static ZoneKind PatchOf(int seed, bool compressedAllowed, int cx, int cy,
                                       out int px, out int py, out int r)
        {
            ulong h = Roll(seed, cx, cy);
            int roll = (int)(h % 100UL);

            ZoneKind z;
            // Стартовые зоны есть в каждом слое: рядом с началом координат всегда можно что-то построить
            if (cx == 0 && cy == 0) z = ZoneKind.HighDensity;
            else if (cx == -1 && cy == 0) z = ZoneKind.Destabilized;
            else if (cx == 0 && cy == -1 && compressedAllowed) z = ZoneKind.Compressed;
            else if (roll < 10) z = ZoneKind.HighDensity;
            else if (roll < 15) z = ZoneKind.Destabilized;
            else if (roll < 17 && compressedAllowed) z = ZoneKind.Compressed;
            else z = ZoneKind.None;

            px = (cx << Chunk.Shift) + 5 + (int)((h >> 8) % 6UL);
            py = (cy << Chunk.Shift) + 5 + (int)((h >> 16) % 6UL);
            r = 3 + (int)((h >> 24) % 3UL);
            return z;
        }

        public static ZoneKind ZoneAt(int seed, bool compressedAllowed, int x, int y)
        {
            int px, py, r;
            ZoneKind z = PatchOf(seed, compressedAllowed, x >> Chunk.Shift, y >> Chunk.Shift, out px, out py, out r);
            if (z == ZoneKind.None) return ZoneKind.None;

            int dx = x - px, dy = y - py;
            return dx * dx + dy * dy <= r * r ? z : ZoneKind.None;
        }

        // Центр пятна нужного вида в чанке (cx, cy); false, если там другой вид зоны или её нет
        public static bool FindCenter(Layout l, ZoneKind zone, int cx, int cy, out int x, out int y)
        {
            int r;
            ZoneKind z = PatchOf(l.Seed, l.CompressedAllowed, cx, cy, out x, out y, out r);
            return z == zone;
        }
    }
}