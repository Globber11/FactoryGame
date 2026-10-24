using UnityEngine;
using Factory.Core;

// Вся визуальная часть вида: процедурные текстуры (внешних ассетов нет) и рисование клеток.
// Только отображение: здесь можно float и Time.time, в ядро это не попадает.
public static class Visuals
{
    // Общий множитель прозрачности (для "призрака" блока под курсором)
    public static float Alpha = 1f;

    // ---------- палитра ----------

    // Фон зависит от глубины: каждый спуск внутрь модуля сдвигает оттенок
    public static Color BackgroundColor(int depth)
    {
        return Color.HSVToRGB(Mathf.Repeat(0.62f + depth * 0.12f, 1f), 0.55f, 0.11f);
    }

    public static Color Accent(CellKind k)
    {
        switch (k)
        {
            case CellKind.Belt: return new Color(0.55f, 0.62f, 0.80f);
            case CellKind.Source: return new Color(0.20f, 0.75f, 0.40f);
            case CellKind.Sink: return new Color(0.90f, 0.30f, 0.35f);
            case CellKind.Module: return new Color(0.60f, 0.42f, 0.95f);
            case CellKind.Input: return new Color(0.20f, 0.75f, 0.80f);
            case CellKind.Output: return new Color(1.00f, 0.62f, 0.20f);
            case CellKind.Extractor: return new Color(0.30f, 0.58f, 1.00f);
            case CellKind.Stabilizer: return new Color(0.35f, 0.85f, 0.50f);
            case CellKind.Fluctuator: return new Color(1.00f, 0.45f, 0.25f);
            case CellKind.QuantumNode: return new Color(1.00f, 0.88f, 0.30f);
            default: return new Color(0.20f, 0.20f, 0.25f);
        }
    }

    static Color ZoneBase(ZoneKind z)
    {
        switch (z)
        {
            case ZoneKind.HighDensity: return new Color(0.25f, 0.55f, 1.00f);
            case ZoneKind.Destabilized: return new Color(1.00f, 0.45f, 0.15f);
            case ZoneKind.Compressed: return new Color(0.75f, 0.30f, 1.00f);
            default: return Color.clear;
        }
    }

    // ---------- процедурные текстуры ----------

    static Texture2D _circle, _glow, _ring, _chevron, _frame;
    static bool _ready;

    public static Texture2D Circle { get { Init(); return _circle; } }
    public static Texture2D Glow { get { Init(); return _glow; } }
    public static Texture2D Ring { get { Init(); return _ring; } }
    public static Texture2D Chevron { get { Init(); return _chevron; } }
    public static Texture2D Frame { get { Init(); return _frame; } }

    static void Init()
    {
        if (_ready) return;
        _ready = true;

        // Диск с мягким краем
        _circle = Make(64, (u, v) => Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * 10f));

        // Радиальное свечение
        _glow = Make(64, (u, v) =>
        {
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
            return a * a;
        });

        // Тонкое кольцо
        _ring = Make(64, (u, v) => Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.82f) * 12f));

        // Шеврон ">" с вершиной справа: |v| = 0.8 * (0.55 - u) на отрезке u от -0.45 до 0.55
        _chevron = Make(64, (u, v) =>
        {
            if (u < -0.45f || u > 0.62f) return 0f;
            float edge = Mathf.Abs(Mathf.Abs(v) - 0.8f * (0.55f - u));
            return Mathf.Clamp01(1f - edge * 10f);
        });

        // Квадратная рамка
        _frame = Make(64, (u, v) =>
        {
            float m = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
            return Mathf.Clamp01(1f - Mathf.Abs(m - 0.86f) * 12f);
        });
    }

    static Texture2D Make(int size, System.Func<float, float, float> alpha)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;

        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f;
            float v = (y + 0.5f) / size * 2f - 1f;
            px[y * size + x] = new Color(1f, 1f, 1f, alpha(u, v));
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // ---------- базовое рисование ----------

    static Color A(Color c)
    {
        c.a *= Alpha;
        return c;
    }

    public static void Fill(Rect r, Color c)
    {
        GUI.color = A(c);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
    }

    public static void Tex(Texture2D tex, Rect r, Color c)
    {
        GUI.color = A(c);
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
    }

    // Текстура, повёрнутая вокруг своего центра (угол в градусах по часовой стрелке)
    public static void Tex(Texture2D tex, Rect r, Color c, float angle)
    {
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, r.center);
        GUI.color = A(c);
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
        GUI.matrix = saved;
    }

    public static void Line(Vector2 a, Vector2 b, float width, Color col)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1f) return;

        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.color = A(col);
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }

    // Скруглённая заливка и рамка (встроенная перегрузка DrawTexture с радиусом углов)
    public static void FillRound(Rect r, Color c, float radius)
    {
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(c), 0f, radius);
    }

    public static void StrokeRound(Rect r, Color c, float width, float radius)
    {
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(c), width, radius);
    }

    static Rect Sq(Vector2 c, float size)
    {
        return new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size);
    }

    static Rect Inset(Rect r, float k)
    {
        return new Rect(r.x + k, r.y + k, r.width - 2f * k, r.height - 2f * k);
    }

    static float AngleOf(Dir d)
    {
        return d == Dir.E ? 0f : d == Dir.S ? 90f : d == Dir.W ? 180f : 270f;
    }

    // Вектор "вперёд" в экранных координатах (Y вниз)
    static Vector2 Fwd(Dir d)
    {
        return new Vector2(DirUtil.Dx(d), -DirUtil.Dy(d));
    }

    // ---------- клетки ----------

    // Бегущие шевроны по ленте (чисто декоративные, медленнее самих предметов)
    static void Flow(Rect r, Dir facing, float time, Color col, float zoom)
    {
        Vector2 fwd = Fwd(facing);
        float ang = AngleOf(facing);
        for (int k = 0; k < 2; k++)
        {
            float t = Mathf.Repeat(time + k * 0.5f, 1f) - 0.5f;   // от -0.5 до 0.5 вдоль клетки
            float fade = 1f - Mathf.Abs(t) * 1.8f;
            if (fade <= 0f) continue;

            Color cc = col;
            cc.a *= fade;
            Tex(Chevron, Sq(r.center + fwd * (t * zoom), zoom * 0.42f), cc, ang);
        }
    }

    static void ArrowFront(Rect r, Dir facing, float zoom, Color col)
    {
        Vector2 c = r.center + Fwd(facing) * (zoom * 0.30f);
        Tex(Chevron, Sq(c, zoom * 0.34f), new Color(col.r, col.g, col.b, 0.95f), AngleOf(facing));
    }

    // r: экранный прямоугольник клетки, zoom: пикселей на клетку, time: Time.time
    // beltMask нужен только для ленты (призрак): какие стороны соединены с соседями
    public static void DrawCell(Cell c, Rect r, float zoom, float time, int wx, int wy, int beltMask = 0)
    {
        if (c.Kind == CellKind.Belt)
        {
            BeltEdge(r, zoom, beltMask);
            BeltBody(c, r, zoom, time, beltMask);
            return;
        }

        Color acc = Accent(c.Kind);
        bool detail = zoom >= 22f;
        bool needsPower = c.Kind == CellKind.Module || c.Kind == CellKind.Extractor;
        bool on = !needsPower || c.Powered != 0;

        Color tile = c.Kind == CellKind.Belt
            ? new Color(0.12f, 0.14f, 0.19f)
            : Color.Lerp(new Color(0.07f, 0.08f, 0.12f), acc, 0.30f);
        if (!on)
        {
            tile = Color.Lerp(tile, new Color(0.05f, 0.05f, 0.08f), 0.55f);
            acc = Color.Lerp(acc, new Color(0.30f, 0.30f, 0.35f), 0.60f);
        }

        Rect body = detail ? Inset(r, 1f) : r;
        Fill(body, tile);

        // Совсем мелкий масштаб: только цветной значок без анимации
        if (zoom < 14f)
        {
            if (c.Kind != CellKind.Belt) Fill(Inset(r, zoom * 0.25f), acc);
            return;
        }

        Tex(Frame, body, new Color(acc.r, acc.g, acc.b, c.Kind == CellKind.Belt ? 0.16f : 0.50f));

        float u = zoom;
        Vector2 ctr = r.center;

        switch (c.Kind)
        {
            case CellKind.Input:
            case CellKind.Output:
                if (detail) Flow(r, c.Facing, time, new Color(acc.r, acc.g, acc.b, 0.75f), u);
                else Tex(Chevron, Sq(ctr, u * 0.55f), new Color(acc.r, acc.g, acc.b, 0.90f), AngleOf(c.Facing));
                break;

            case CellKind.Source:
            {
                float p = 0.5f + 0.5f * Mathf.Sin(time * 3f);
                Tex(Glow, Sq(ctr, u * (1.1f + 0.15f * p)), new Color(acc.r, acc.g, acc.b, 0.35f));
                Tex(Circle, Sq(ctr, u * (0.34f + 0.05f * p)), new Color(acc.r, acc.g, acc.b, 1f));
                ArrowFront(r, c.Facing, u, acc);
                break;
            }

            case CellKind.Sink:
            {
                // Две волны, стягивающиеся к центру: "воронка"
                Tex(Glow, Sq(ctr, u * 1.15f), new Color(acc.r, acc.g, acc.b, 0.30f));
                float p = Mathf.Repeat(time * 0.8f, 1f);
                float q = Mathf.Repeat(time * 0.8f + 0.5f, 1f);
                Tex(Ring, Sq(ctr, u * (0.95f - 0.6f * p)), new Color(acc.r, acc.g, acc.b, (1f - p) * 0.9f));
                Tex(Ring, Sq(ctr, u * (0.95f - 0.6f * q)), new Color(acc.r, acc.g, acc.b, (1f - q) * 0.9f));
                Tex(Circle, Sq(ctr, u * 0.22f), new Color(0.02f, 0.02f, 0.04f, 1f));
                break;
            }

            case CellKind.Module:
            {
                // Вложенные рамки: "пространство внутри пространства"
                Tex(Frame, Inset(r, u * 0.16f), new Color(acc.r, acc.g, acc.b, on ? 0.90f : 0.45f));
                Tex(Frame, Inset(r, u * 0.30f), new Color(acc.r, acc.g, acc.b, on ? 0.60f : 0.30f));
                if (on)
                {
                    float p = 0.5f + 0.5f * Mathf.Sin(time * 2f + wx);
                    Tex(Glow, Sq(ctr, u * 1.25f), new Color(acc.r, acc.g, acc.b, 0.18f + 0.14f * p));
                    Tex(Circle, Sq(ctr, u * (0.16f + 0.04f * p)), new Color(0.90f, 0.95f, 1f, 0.95f));
                }
                else
                {
                    // Красная точка в углу: нет питания
                    Tex(Circle, Sq(new Vector2(r.xMax - u * 0.2f, r.y + u * 0.2f), u * 0.16f), new Color(1f, 0.25f, 0.20f, 1f));
                }
                break;
            }

            case CellKind.Extractor:
            {
                if (on)
                {
                    float p = 0.5f + 0.5f * Mathf.Sin(time * 4f);
                    Tex(Glow, Sq(ctr, u * 1.1f), new Color(acc.r, acc.g, acc.b, 0.22f + 0.10f * p));
                    Tex(Frame, Inset(r, u * 0.20f), new Color(acc.r, acc.g, acc.b, 0.90f), time * 70f);
                    Tex(Frame, Inset(r, u * 0.30f), new Color(acc.r, acc.g, acc.b, 0.60f), -time * 40f);
                }
                else
                {
                    Tex(Frame, Inset(r, u * 0.22f), new Color(acc.r, acc.g, acc.b, 0.50f), 45f);
                }
                Tex(Circle, Sq(ctr, u * 0.16f), new Color(0.90f, 0.95f, 1f, on ? 1f : 0.4f));
                ArrowFront(r, c.Facing, u, acc);
                break;
            }

            case CellKind.Stabilizer:
            {
                Tex(Ring, Inset(r, u * 0.14f), new Color(acc.r, acc.g, acc.b, 0.85f));
                Color hi = new Color(1f, 0.97f, 0.70f);
                Color lo = new Color(0.40f, 1f, 0.50f);
                if (c.Timer == StabilizerMode.Both)
                {
                    Tex(Circle, Sq(ctr + new Vector2(-u * 0.12f, 0f), u * 0.22f), hi);
                    Tex(Circle, Sq(ctr + new Vector2(u * 0.12f, 0f), u * 0.22f), lo);
                }
                else
                {
                    Color core = c.Timer == StabilizerMode.Lower ? lo : hi;
                    Tex(Glow, Sq(ctr, u * 0.8f), new Color(core.r, core.g, core.b, 0.35f));
                    Tex(Circle, Sq(ctr, u * 0.30f), core);
                }
                ArrowFront(r, c.Facing, u, acc);
                break;
            }

            case CellKind.Fluctuator:
            {
                float fl = 0.65f + 0.35f * Mathf.Sin(time * 9f + wx * 1.7f + wy);
                Tex(Glow, Sq(ctr, u * (1.2f + 0.2f * fl)), new Color(acc.r, acc.g, acc.b, 0.30f + 0.25f * fl));
                Tex(Frame, Inset(r, u * 0.22f), new Color(acc.r, acc.g, acc.b, 0.90f), time * 120f);
                Tex(Frame, Inset(r, u * 0.22f), new Color(1f, 0.85f, 0.60f, 0.70f), -time * 80f + 20f);
                Tex(Circle, Sq(ctr, u * (0.20f + 0.08f * fl)), new Color(1f, 0.95f, 0.85f, 1f));
                break;
            }

            case CellKind.QuantumNode:
            {
                float p = Mathf.Repeat(time * 0.6f + wx * 0.13f, 1f);
                Tex(Glow, Sq(ctr, u * 1.3f), new Color(acc.r, acc.g, acc.b, 0.35f));
                Tex(Ring, Sq(ctr, u * (0.5f + 1.6f * p)), new Color(acc.r, acc.g, acc.b, (1f - p) * 0.8f));
                Tex(Frame, Inset(r, u * 0.26f), new Color(acc.r, acc.g, acc.b, 0.95f), 45f);
                Tex(Circle, Sq(ctr, u * 0.22f), new Color(1f, 0.98f, 0.80f, 1f));
                break;
            }
        }
    }

    // ---------- цельные ленты ----------
    // Лента рисуется не плиткой, а "дорожкой": центральный квадрат и рукава к соединённым сторонам.
    // Соседние ленты стыкуются без швов, на поворотах дорожка изгибается.
    // mask: бит (1 << (int)Dir) на каждую соединённую сторону (считает SimRunner.BeltMask).

    static readonly Rect[] _lane = new Rect[5];

    static bool Has(int mask, Dir d)
    {
        return (mask & (1 << (int)d)) != 0;
    }

    // Откуда в клетку входят предметы: сзади, а если сзади ничего нет, то сбоку (поворот)
    public static Dir EntryDir(Dir facing, int mask)
    {
        Dir back = DirUtil.Opposite(facing);
        if (Has(mask, back)) return back;

        Dir left = (Dir)(((int)facing + 3) & 3);
        Dir right = (Dir)(((int)facing + 1) & 3);
        if (Has(mask, left)) return left;
        if (Has(mask, right)) return right;
        return back;
    }

    // Положение предмета на ленте: от границы клетки (вход) через центр к границе (выход), t от 0 до 1.
    // Путь идёт границами, поэтому на стыке клеток скачка нет: выход одной клетки это вход следующей
    public static Vector2 BeltPos(Rect r, float zoom, Dir facing, int mask, float t)
    {
        Vector2 c = r.center;
        Vector2 p0 = c + Fwd(EntryDir(facing, mask)) * (zoom * 0.5f);
        Vector2 p1 = c + Fwd(facing) * (zoom * 0.5f);
        return t < 0.5f ? Vector2.Lerp(p0, c, t * 2f) : Vector2.Lerp(c, p1, (t - 0.5f) * 2f);
    }

    // Прямоугольники дорожки; expand раздувает их для рамки. Возвращает число прямоугольников в _lane
    static int BuildLane(Rect r, float zoom, int mask, float expand)
    {
        float lane = Mathf.Max(2f, Mathf.Round(zoom * 0.56f));
        Vector2 c = r.center;
        float x0 = Mathf.Round(c.x - lane * 0.5f);
        float y0 = Mathf.Round(c.y - lane * 0.5f);
        float lx0 = x0 - expand, lx1 = x0 + lane + expand;
        float ly0 = y0 - expand, ly1 = y0 + lane + expand;

        int n = 0;
        _lane[n++] = new Rect(lx0, ly0, lx1 - lx0, ly1 - ly0);

        // Рукава до границы клетки (плюс 1 px, чтобы между соседями не было щелей)
        if (Has(mask, Dir.N)) _lane[n++] = new Rect(lx0, r.y - 1f, lx1 - lx0, ly0 + 1f - (r.y - 1f));
        if (Has(mask, Dir.S)) _lane[n++] = new Rect(lx0, ly1 - 1f, lx1 - lx0, r.yMax + 1f - (ly1 - 1f));
        if (Has(mask, Dir.W)) _lane[n++] = new Rect(r.x - 1f, ly0, lx0 + 1f - (r.x - 1f), ly1 - ly0);
        if (Has(mask, Dir.E)) _lane[n++] = new Rect(lx1 - 1f, ly0, r.xMax + 1f - (lx1 - 1f), ly1 - ly0);
        return n;
    }

    // Проход 1: рамка дорожки. Рисуется для ВСЕХ лент до заливок, чтобы рамка не перекрывала соседнюю ленту
    public static void BeltEdge(Rect r, float zoom, int mask)
    {
        float b = Mathf.Max(1f, Mathf.Round(zoom * 0.05f));
        int n = BuildLane(r, zoom, mask, b);
        Color col = new Color(0.32f, 0.40f, 0.60f, 0.95f);
        for (int i = 0; i < n; i++) Fill(_lane[i], col);
    }

    // Проход 2: заливка дорожки и бегущие шевроны (идут по изгибу)
    public static void BeltBody(Cell c, Rect r, float zoom, float time, int mask)
    {
        int n = BuildLane(r, zoom, mask, 0f);
        Color fill = new Color(0.10f, 0.12f, 0.17f);
        for (int i = 0; i < n; i++) Fill(_lane[i], fill);

        if (zoom < 14f) return;

        Dir entry = EntryDir(c.Facing, mask);
        float angIn = AngleOf(DirUtil.Opposite(entry));   // от входа к центру
        float angOut = AngleOf(c.Facing);                 // от центра к выходу
        Color col = new Color(0.62f, 0.70f, 0.92f, 0.32f);

        for (int k = 0; k < 2; k++)
        {
            float u = Mathf.Repeat(time + k * 0.5f, 1f);
            float fade = 1f - Mathf.Abs(u - 0.5f) * 2f;   // на границах клетки шеврон невидим
            Color cc = col;
            cc.a *= fade;
            Tex(Chevron, Sq(BeltPos(r, zoom, c.Facing, mask, u), zoom * 0.34f), cc, u < 0.5f ? angIn : angOut);
        }
    }

    // Предмет: свечение, диск и блик
    public static void DrawItem(Vector2 center, float size, Color col)
    {
        Tex(Glow, Sq(center, size * 2.4f), new Color(col.r, col.g, col.b, 0.40f));
        Tex(Circle, Sq(center, size), col);
        Tex(Circle, Sq(center - new Vector2(size * 0.12f, size * 0.12f), size * 0.4f), new Color(1f, 1f, 1f, 0.55f));
    }

    // ---------- фон и зоны ----------

    static uint Hash(int a, int b, int c, int d)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u + (uint)d * 3266489917u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    // Мерцающие звёзды в чанке (позиции из хэша, поэтому не прыгают при движении камеры)
    public static void Stars(Rect chunkRect, int cx, int cy, int depth, float time)
    {
        for (int k = 0; k < 4; k++)
        {
            uint h = Hash(cx, cy, depth, k);
            float px = (h & 0xFFFFu) / 65535f;
            float py = ((h >> 16) & 0xFFFFu) / 65535f;
            float size = 1.5f + ((h >> 8) & 3u) * 0.7f;
            float tw = 0.30f + 0.22f * Mathf.Sin(time * (0.6f + (h & 7u) * 0.12f) + k * 2.1f);
            Vector2 p = new Vector2(chunkRect.x + px * chunkRect.width, chunkRect.y + py * chunkRect.height);
            Tex(Circle, Sq(p, size), new Color(0.75f, 0.85f, 1f, tw));
        }
    }

    // Клетка зоны: радиальный градиент, яркая кромка, мерцание и значок вида зоны
    public static void ZoneCell(ZoneKind z, Rect r, float zoom, float time, int wx, int wy, float k, bool rim, bool glyphs)
    {
        Color zc = ZoneBase(z);
        float shimmer = 0.85f + 0.15f * Mathf.Sin(time * 1.3f + wx * 0.6f + wy * 0.8f);
        float a = Mathf.Lerp(0.34f, 0.12f, k) * shimmer;
        if (rim) a += 0.14f;
        Fill(r, new Color(zc.r, zc.g, zc.b, a));
        if (!glyphs) return;

        Vector2 c = r.center;
        switch (z)
        {
            case ZoneKind.HighDensity:
                // Плотные частицы
                Tex(Circle, Sq(c, zoom * 0.10f), new Color(0.70f, 0.85f, 1f, 0.40f * shimmer));
                break;

            case ZoneKind.Destabilized:
            {
                // Расходящиеся кольца нестабильности
                float p = Mathf.Repeat(time * 0.5f + (wx * 7 + wy * 13) * 0.071f, 1f);
                Tex(Ring, Sq(c, zoom * (0.15f + 0.35f * p)), new Color(1f, 0.70f, 0.40f, (1f - p) * 0.5f));
                break;
            }

            case ZoneKind.Compressed:
                // Сжатая рамка
                Tex(Frame, Sq(c, zoom * 0.30f), new Color(0.90f, 0.70f, 1f, 0.35f * shimmer), 45f);
                break;
        }
    }

    // ---------- энергосеть ----------

    // Связь узла с элементом: тусклая линия и бегущие огоньки, если связь активна
    public static void Link(Vector2 from, Vector2 to, float width, bool active, float time, float seed)
    {
        Color baseCol = active ? new Color(1f, 0.90f, 0.25f) : new Color(0.90f, 0.30f, 0.25f);
        Line(from, to, width, new Color(baseCol.r, baseCol.g, baseCol.b, active ? 0.28f : 0.20f));
        if (!active) return;

        for (int k = 0; k < 3; k++)
        {
            float t = Mathf.Repeat(time * 0.5f + k / 3f + seed, 1f);
            Tex(Glow, Sq(Vector2.Lerp(from, to, t), Mathf.Max(8f, width * 5f)), new Color(1f, 0.95f, 0.50f, 0.85f));
        }
    }

    // Радиус действия узла: окружность из отрезков
    public static void RadiusRing(Vector2 center, float radius, float alpha)
    {
        const int seg = 56;
        Color col = new Color(1f, 0.90f, 0.30f, alpha);
        Vector2 prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float ang = i * (Mathf.PI * 2f / seg);
            Vector2 p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
            Line(prev, p, 2f, col);
            prev = p;
        }
    }

    // ---------- оформление кнопок IMGUI ----------

    static bool _skinned;

    static Texture2D RoundTex(Color fill, Color border)
    {
        const int size = 32;
        const float radius = 9f;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;

        var px = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
            float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;   // меньше 0 внутри

            Color col = Color.Lerp(fill, border, Mathf.Clamp01(d + 1.6f));
            col.a *= Mathf.Clamp01(0.5f - d);
            px[y * size + x] = col;
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // Один раз заменяет вид кнопок и панели инструментов на тёмный с подсветкой выбранного
    public static void ApplySkin()
    {
        if (_skinned) return;
        _skinned = true;

        GUIStyle b = GUI.skin.button;
        b.border = new RectOffset(10, 10, 10, 10);

        b.normal.background = RoundTex(new Color(0.12f, 0.14f, 0.21f, 0.96f), new Color(0.28f, 0.33f, 0.50f, 1f));
        b.hover.background = RoundTex(new Color(0.18f, 0.21f, 0.31f, 0.98f), new Color(0.42f, 0.50f, 0.75f, 1f));
        b.active.background = RoundTex(new Color(0.26f, 0.31f, 0.45f, 1f), new Color(0.60f, 0.70f, 1f, 1f));
        b.onNormal.background = RoundTex(new Color(0.18f, 0.34f, 0.68f, 1f), new Color(0.50f, 0.70f, 1f, 1f));
        b.onHover.background = RoundTex(new Color(0.22f, 0.40f, 0.76f, 1f), new Color(0.65f, 0.80f, 1f, 1f));
        b.onActive.background = RoundTex(new Color(0.26f, 0.46f, 0.84f, 1f), new Color(0.75f, 0.88f, 1f, 1f));

        Color txt = new Color(0.86f, 0.90f, 1f);
        b.normal.textColor = txt;
        b.hover.textColor = Color.white;
        b.active.textColor = Color.white;
        b.onNormal.textColor = Color.white;
        b.onHover.textColor = Color.white;
        b.onActive.textColor = Color.white;

        GUI.skin.label.normal.textColor = new Color(0.87f, 0.91f, 1f);
    }
}
