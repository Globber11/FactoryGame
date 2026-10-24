namespace Factory.Core
{
    public sealed class Chunk
    {
        public const int Shift = 4;
        public const int Size = 1 << Shift;   // 16
        public const int Mask = Size - 1;
        public const int CellCount = Size * Size;

        public readonly int Cx, Cy;
        public Cell[] Cells = new Cell[CellCount];
        public Cell[] Next = new Cell[CellCount];
        public int Occupied;   // сколько клеток не Empty

        public Chunk(int cx, int cy) { Cx = cx; Cy = cy; }
    }
}