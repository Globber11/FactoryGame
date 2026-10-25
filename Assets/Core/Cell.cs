namespace Factory.Core
{
    public enum Dir : byte { N, E, S, W }

    public enum CellKind : byte
    {
        Empty, Belt, Source, Sink, Module, Input, Output,
        Extractor,     // добывает нестабильную эссенцию; только на области высокой плотности; нужна энергия
        Stabilizer,    // нестабильная эссенция -> высшая или низшая (режим хранится в Timer)
        Fluctuator,    // вырабатывает квантовую энергию; только на дестабилизированной области
        QuantumNode    // связывает флуктуаторы и потребителей энергии в радиусе
    }

    // Зона пространства. В клетках НЕ хранится: вычисляется из seed слоя и координат (см. ZoneGenerator)
    public enum ZoneKind : byte
    {
        None,
        HighDensity,    // область высокой плотности реальности: экстракторы
        Destabilized,   // деструктурированная область: флуктуаторы
        Compressed      // сжатое пространство (только внутри модулей), пока ничего не делает
    }

    public struct Cell
    {
        public CellKind Kind;
        public Dir Facing;
        public byte Item;      // 0 = пусто
        public byte Powered;   // у Module и Extractor: 1 = питание есть (выставляет EnergyNetwork перед тактом)
        public int Timer;      // Source/Extractor: таймер; Module: указатель очереди по входам; Stabilizer: режим (StabilizerMode)
        public int Link;       // Module: id вложенного слоя; Stabilizer: какой вид выдать следующим в режиме "оба"; Belt/Input: предметы (BeltSlot)
    }

    // Предметы. Все ресурсы игры называются эссенциями
    public static class Items
    {
        public const byte Basic = 1;             // отладочный предмет из Source
        // 2 свободен (раньше так была закодирована энергия)
        public const byte UnstableEssence = 3;   // нестабильная эссенция пространства
        public const byte HigherEssence = 4;     // высшая эссенция пространства (исследования, позже)
        public const byte LowerEssence = 5;      // низшая эссенция пространства (строительство)

        public static string Name(byte item)
        {
            switch (item)
            {
                case Basic: return "обычный";
                case UnstableEssence: return "нестабильная эссенция";
                case HigherEssence: return "высшая эссенция";
                case LowerEssence: return "низшая эссенция";
                default: return "?";
            }
        }
    }

    // Режимы стабилизатора (хранятся в Cell.Timer)
    public static class StabilizerMode
    {
        public const int Higher = 0;   // выдаёт высшую эссенцию
        public const int Lower = 1;    // выдаёт низшую эссенцию
        public const int Both = 2;     // выдаёт оба вида по очереди
        public const int Count = 3;

        public static string Name(int mode)
        {
            switch (mode)
            {
                case Higher: return "высшая эссенция";
                case Lower: return "низшая эссенция";
                case Both: return "обе (по очереди)";
                default: return "?";
            }
        }
    }

    // Лента хранит до четырёх предметов в клетке: места 0 (сзади, у входа) .. 3 (спереди, у выхода).
    // Каждое место это байт в Cell.Link (0 = пусто). Так плотность в 4 раза выше, чем у одного предмета на клетку.
    public static class BeltSlot
    {
        public const int Count = 4;

        public static byte Get(int packed, int slot)
        {
            return (byte)((packed >> (slot * 8)) & 0xFF);
        }

        public static int Set(int packed, int slot, byte item)
        {
            int shift = slot * 8;
            return (packed & ~(0xFF << shift)) | (item << shift);
        }

        public static int CountItems(int packed)
        {
            int n = 0;
            for (int s = 0; s < Count; s++)
                if (Get(packed, s) != 0) n++;
            return n;
        }
    }

    public static class DirUtil
    {
        // N = y+1 (ось Y направлена вверх)
        public static int Dx(Dir d) => d == Dir.E ? 1 : d == Dir.W ? -1 : 0;
        public static int Dy(Dir d) => d == Dir.N ? 1 : d == Dir.S ? -1 : 0;
        public static Dir Opposite(Dir d) => (Dir)(((int)d + 2) & 3);
    }
}