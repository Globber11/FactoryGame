using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Factory.Core;

public class SimRunner : MonoBehaviour
{
    const float TickSeconds = 0.1f;
    const int MaxTicksPerFrame = 5;

    const float MinZoom = 8f;
    const float MaxZoom = 96f;
    const float DefaultZoom = 40f;
    const float MaxFitZoom = 64f;
    const float PanSpeedPx = 700f;   // скорость камеры, пикселей в секунду
    const int PanelH = 278;          // высота нижней панели
    const int Pad = 12;

    enum Tool { Belt, Source, Sink, Module, Input, Output, Enter, Extractor, Stabilizer, Fluctuator, QuantumNode }
    static readonly string[] ToolNames =
        { "1 Лента", "2 Источник", "3 Приёмник", "4 Модуль", "5 Вход", "6 Выход", "7 Войти",
          "8 Экстрактор", "9 Стабилиз.", "0 Флуктуатор", "- Узел" };
    static readonly KeyCode[] ToolKeys =
        { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
          KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus };

    enum DragMode { None, Pan, Paint, Erase }

    // Один кадр навигации: в каком слое мы, где камера и какой масштаб (центр камеры в клетках)
    sealed class Frame
    {
        public int LayoutId;
        public float CamX, CamY;
        public float Zoom = DefaultZoom;   // пикселей на клетку
    }

    static readonly Color ChunkA = new Color(0.17f, 0.17f, 0.19f);
    static readonly Color ChunkB = new Color(0.12f, 0.12f, 0.14f);

    World _world;
    readonly List<Frame> _stack = new List<Frame>();
    readonly HashSet<KeyCode> _down = new HashSet<KeyCode>();
    Tool _tool = Tool.Belt;
    Dir _facing = Dir.E;
    DragMode _mode = DragMode.None;
    int _lastX, _lastY;
    float _acc;
    bool _paused;
    string _status = "";
    CostInfo _cost;
    GUIStyle _cellStyle;

    Frame Top => _stack[_stack.Count - 1];

    // ---------- демо-мир ----------

    struct Op { public int X, Y; public CellKind Kind; public Dir Facing; }

    static Op MakeOp(int x, int y, CellKind k, Dir d)
    {
        return new Op { X = x, Y = y, Kind = k, Facing = d };
    }

    static void Apply(World w, Layout l, List<Op> ops, bool reverse)
    {
        if (reverse)
            for (int i = ops.Count - 1; i >= 0; i--) w.Place(l, ops[i].X, ops[i].Y, ops[i].Kind, ops[i].Facing);
        else
            for (int i = 0; i < ops.Count; i++) w.Place(l, ops[i].X, ops[i].Y, ops[i].Kind, ops[i].Facing);
    }

    // reverse = строить в обратном порядке (проверка, что порядок постройки не влияет на результат)
    static World BuildDemo(bool reverse)
    {
        var w = new World();
        var ops = new List<Op>();

        // Линия обычных предметов через модуль
        ops.Add(MakeOp(-5, 2, CellKind.Source, Dir.E));
        for (int x = -4; x <= 3; x++) ops.Add(MakeOp(x, 2, CellKind.Belt, Dir.E));
        ops.Add(MakeOp(4, 2, CellKind.Module, Dir.N));
        for (int x = 5; x <= 19; x++) ops.Add(MakeOp(x, 2, CellKind.Belt, Dir.E));
        ops.Add(MakeOp(20, 2, CellKind.Sink, Dir.E));

        // Энергия: флуктуатор в центре дестабилизированной области и один квантовый узел,
        // радиус которого достаёт и до флуктуатора, и до модуля, и до экстрактора
        int fx, fy, hx, hy;
        if (ZoneGenerator.FindCenter(w.Root, ZoneKind.Destabilized, -1, 0, out fx, out fy))
            ops.Add(MakeOp(fx, fy, CellKind.Fluctuator, Dir.N));
        ops.Add(MakeOp(0, 7, CellKind.QuantumNode, Dir.N));

        // Эссенции: экстрактор в центре области высокой плотности -> лента -> стабилизатор -> ленты -> приёмник
        if (ZoneGenerator.FindCenter(w.Root, ZoneKind.HighDensity, 0, 0, out hx, out hy))
        {
            ops.Add(MakeOp(hx, hy, CellKind.Extractor, Dir.E));
            ops.Add(MakeOp(hx + 1, hy, CellKind.Belt, Dir.E));
            ops.Add(MakeOp(hx + 2, hy, CellKind.Stabilizer, Dir.E));
            for (int x = hx + 3; x <= hx + 5; x++) ops.Add(MakeOp(x, hy, CellKind.Belt, Dir.E));
            ops.Add(MakeOp(hx + 6, hy, CellKind.Sink, Dir.E));
        }
        Apply(w, w.Root, ops, reverse);

        // Внутри модуля: вход -> лента -> выход (наружу через восточную сторону)
        Layout inner = w.Get(w.Root.Get(4, 2).Link);
        ops.Clear();
        ops.Add(MakeOp(0, 0, CellKind.Input, Dir.E));
        for (int x = 1; x <= 4; x++) ops.Add(MakeOp(x, 0, CellKind.Belt, Dir.E));
        ops.Add(MakeOp(5, 0, CellKind.Output, Dir.E));
        Apply(w, inner, ops, reverse);

        return w;
    }

    static bool DeterminismCheck()
    {
        World a = BuildDemo(false), b = BuildDemo(true);
        for (int i = 0; i < 1000; i++) { a.Step(); b.Step(); }
        return a.Hash() == b.Hash();
    }

    // ---------- жизненный цикл ----------

    void Start() => Reset();

    void OnApplicationFocus(bool hasFocus)
    {
        // Чтобы клавиши не "залипали", если окно потеряло фокус
        if (!hasFocus) { _down.Clear(); _mode = DragMode.None; }
    }

    static Rect ViewRect()
    {
        return new Rect(0, 0, Screen.width, Mathf.Max(120, Screen.height - PanelH));
    }

    void Reset()
    {
        _world = BuildDemo(false);
        _stack.Clear();
        var f = new Frame { LayoutId = 0 };
        _stack.Add(f);
        FocusContent(f, _world.Root);
        _down.Clear();
        _mode = DragMode.None;
        _acc = 0;
        _status = "";
    }

    void Update()
    {
        if (_world == null) return;

        UpdateCamera(Time.unscaledDeltaTime);

        if (_paused) return;
        _acc += Time.deltaTime;
        int n = 0;
        while (_acc >= TickSeconds && n < MaxTicksPerFrame)
        {
            _acc -= TickSeconds;
            _world.Step();
            n++;
        }
    }

    void UpdateCamera(float dt)
    {
        float dx = 0, dy = 0;
        if (_down.Contains(KeyCode.A) || _down.Contains(KeyCode.LeftArrow)) dx -= 1;
        if (_down.Contains(KeyCode.D) || _down.Contains(KeyCode.RightArrow)) dx += 1;
        if (_down.Contains(KeyCode.W) || _down.Contains(KeyCode.UpArrow)) dy += 1;
        if (_down.Contains(KeyCode.S) || _down.Contains(KeyCode.DownArrow)) dy -= 1;
        if (dx == 0 && dy == 0) return;

        if (dx != 0 && dy != 0) { dx *= 0.7071f; dy *= 0.7071f; }
        bool fast = _down.Contains(KeyCode.LeftShift) || _down.Contains(KeyCode.RightShift);

        Frame f = Top;
        float k = PanSpeedPx * (fast ? 3f : 1f) * dt / f.Zoom;
        f.CamX += dx * k;
        f.CamY += dy * k;
    }

    // ---------- навигация ----------

    void Exit()
    {
        if (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1);
        _mode = DragMode.None;
    }

    void TryEnter(Layout layout, int x, int y)
    {
        Cell c = layout.Get(x, y);
        if (c.Kind != CellKind.Module) { _status = "Здесь нет модуля."; return; }
        Layout child = _world.Get(c.Link);
        if (child == null) return;

        var f = new Frame { LayoutId = c.Link };
        FocusContent(f, child);
        _stack.Add(f);
        _mode = DragMode.None;
    }

    // Ставит камеру на середину содержимого слоя и подбирает масштаб
    void FocusContent(Frame f, Layout layout)
    {
        bool any = false;
        int minX = 0, maxX = 0, minY = 0, maxY = 0;

        var chunks = layout.Chunks;
        for (int n = 0; n < chunks.Count; n++)
        {
            Chunk ch = chunks[n];
            int baseX = ch.Cx << Chunk.Shift;
            int baseY = ch.Cy << Chunk.Shift;
            for (int i = 0; i < Chunk.CellCount; i++)
            {
                if (ch.Cells[i].Kind == CellKind.Empty) continue;
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
            }
        }

        if (!any)
        {
            f.CamX = 0.5f; f.CamY = 0.5f; f.Zoom = DefaultZoom;
            return;
        }

        f.CamX = (minX + maxX + 1) * 0.5f;
        f.CamY = (minY + maxY + 1) * 0.5f;

        Rect view = ViewRect();
        float w = (maxX - minX + 1) + 4;
        float h = (maxY - minY + 1) + 4;
        f.Zoom = Mathf.Clamp(Mathf.Min(view.width / w, view.height / h), MinZoom, MaxFitZoom);
    }

    string BreadcrumbText()
    {
        var sb = new StringBuilder("Мир");
        for (int i = 1; i < _stack.Count; i++) sb.Append(" > Слой ").Append(_stack[i].LayoutId);
        return sb.ToString();
    }

    // ---------- преобразования координат ----------

    // Экранный прямоугольник клетки (с целыми границами, чтобы клетки стыковались без щелей)
    static Rect CellRect(Frame f, Rect view, int wx, int wy)
    {
        float cx = view.width * 0.5f, cy = view.height * 0.5f;
        float sx = cx + (wx - f.CamX) * f.Zoom;
        float sy = cy - (wy + 1 - f.CamY) * f.Zoom;   // верхняя граница клетки
        float x0 = Mathf.Floor(sx), y0 = Mathf.Floor(sy);
        float x1 = Mathf.Floor(sx + f.Zoom), y1 = Mathf.Floor(sy + f.Zoom);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    static void ScreenToCell(Frame f, Rect view, Vector2 m, out int x, out int y)
    {
        x = Mathf.FloorToInt(f.CamX + (m.x - view.width * 0.5f) / f.Zoom);
        y = Mathf.FloorToInt(f.CamY - (m.y - view.height * 0.5f) / f.Zoom);
    }

    static void ZoomAt(Frame f, Rect view, Vector2 m, float factor)
    {
        float cx = view.width * 0.5f, cy = view.height * 0.5f;
        float wx = f.CamX + (m.x - cx) / f.Zoom;
        float wy = f.CamY - (m.y - cy) / f.Zoom;
        f.Zoom = Mathf.Clamp(f.Zoom * factor, MinZoom, MaxZoom);
        f.CamX = wx - (m.x - cx) / f.Zoom;
        f.CamY = wy + (m.y - cy) / f.Zoom;
    }

    // ---------- ввод ----------

    void HandleInput(Event e, Rect view)
    {
        switch (e.type)
        {
            case EventType.KeyDown: OnKeyDown(e, view); break;
            case EventType.KeyUp: _down.Remove(e.keyCode); break;
            case EventType.ScrollWheel:
                if (view.Contains(e.mousePosition))
                {
                    ZoomAt(Top, view, e.mousePosition, e.delta.y < 0 ? 1.15f : 1f / 1.15f);
                    e.Use();
                }
                break;
            case EventType.MouseDown: OnMouseDown(e, view); break;
            case EventType.MouseDrag: OnMouseDrag(e, view); break;
            case EventType.MouseUp: _mode = DragMode.None; break;   // не гасим событие: кнопки панели тоже его ждут
        }
    }

    void OnKeyDown(Event e, Rect view)
    {
        KeyCode k = e.keyCode;
        if (k == KeyCode.None) return;

        // Повтор от удержания клавиши игнорируем: действие срабатывает один раз за нажатие
        if (!_down.Add(k)) { e.Use(); return; }

        Frame f = Top;
        Layout layout = _world.Get(f.LayoutId);
        if (layout == null) return;

        switch (k)
        {
            case KeyCode.W: case KeyCode.A: case KeyCode.S: case KeyCode.D:
            case KeyCode.UpArrow: case KeyCode.DownArrow: case KeyCode.LeftArrow: case KeyCode.RightArrow:
            case KeyCode.Space:
                e.Use();   // само движение камеры идёт в Update
                break;

            case KeyCode.R:
                _facing = (Dir)(((int)_facing + (e.shift ? 3 : 1)) & 3);
                e.Use();
                break;

            case KeyCode.Q:
            case KeyCode.Escape:
            case KeyCode.Backspace:
                Exit(); e.Use();
                break;

            case KeyCode.E:
                if (view.Contains(e.mousePosition))
                {
                    int x, y;
                    ScreenToCell(f, view, e.mousePosition, out x, out y);
                    TryEnter(layout, x, y);
                }
                e.Use();
                break;

            case KeyCode.F: FocusContent(f, layout); e.Use(); break;
            case KeyCode.P: _paused = !_paused; e.Use(); break;

            default:
                int n = System.Array.IndexOf(ToolKeys, k);
                if (n >= 0 && n < ToolNames.Length) { _tool = (Tool)n; e.Use(); }
                break;
        }
    }

    void OnMouseDown(Event e, Rect view)
    {
        if (!view.Contains(e.mousePosition) || HudRect(view).Contains(e.mousePosition)) { _mode = DragMode.None; return; }

        Frame f = Top;
        Layout layout = _world.Get(f.LayoutId);
        if (layout == null) return;

        bool pan = e.button == 2 || (e.button == 0 && _down.Contains(KeyCode.Space));
        if (pan) { _mode = DragMode.Pan; e.Use(); return; }

        int x, y;
        ScreenToCell(f, view, e.mousePosition, out x, out y);

        if (e.button == 0)
        {
            PaintDown(e, layout, x, y);
        }
        else if (e.button == 1)
        {
            _mode = DragMode.Erase;
            _lastX = x; _lastY = y;
            EraseCell(e, layout, x, y, true);
        }
        e.Use();
    }

    void OnMouseDrag(Event e, Rect view)
    {
        if (_mode == DragMode.None) return;

        Frame f = Top;
        Layout layout = _world.Get(f.LayoutId);
        if (layout == null) return;

        if (_mode == DragMode.Pan)
        {
            f.CamX -= e.delta.x / f.Zoom;
            f.CamY += e.delta.y / f.Zoom;
            e.Use();
            return;
        }

        if (!view.Contains(e.mousePosition)) return;

        int x, y;
        ScreenToCell(f, view, e.mousePosition, out x, out y);
        if (_mode == DragMode.Paint) PaintDrag(layout, x, y);
        else EraseDrag(e, layout, x, y);
        e.Use();
    }

    // Один шаг по клеткам в сторону цели (без диагоналей), чтобы протяжка не оставляла дыр
    static bool StepToward(ref int cx, ref int cy, int x, int y, out Dir d)
    {
        int dx = x - cx, dy = y - cy;
        d = Dir.N;
        if (dx == 0 && dy == 0) return false;

        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) d = dx > 0 ? Dir.E : Dir.W;
        else d = dy > 0 ? Dir.N : Dir.S;

        cx += DirUtil.Dx(d);
        cy += DirUtil.Dy(d);
        return true;
    }

    static CellKind ToKind(Tool t)
    {
        switch (t)
        {
            case Tool.Belt: return CellKind.Belt;
            case Tool.Source: return CellKind.Source;
            case Tool.Sink: return CellKind.Sink;
            case Tool.Input: return CellKind.Input;
            case Tool.Output: return CellKind.Output;
            case Tool.Extractor: return CellKind.Extractor;
            case Tool.Stabilizer: return CellKind.Stabilizer;
            case Tool.Fluctuator: return CellKind.Fluctuator;
            case Tool.QuantumNode: return CellKind.QuantumNode;
            default: return CellKind.Module;
        }
    }

    void PaintDown(Event e, Layout layout, int x, int y)
    {
        _mode = DragMode.None;
        Cell cur = layout.Get(x, y);

        if (cur.Kind == CellKind.Module)
        {
            if (_tool == Tool.Enter || e.clickCount >= 2) TryEnter(layout, x, y);
            else _status = "Модуль: двойной клик или E - войти, Shift+ПКМ - удалить.";
            return;
        }
        // Клик по стабилизатору (инструментом "Стабилиз." с тем же поворотом или "Войти") меняет его режим
        if (cur.Kind == CellKind.Stabilizer && (_tool == Tool.Enter || (_tool == Tool.Stabilizer && cur.Facing == _facing)))
        {
            int mode = _world.CycleStabilizerMode(layout, x, y);
            _status = "Режим стабилизатора: " + StabilizerMode.Name(mode);
            return;
        }
        if (_tool == Tool.Enter) { _status = "Здесь нет модуля."; return; }

        PlaceCell(layout, x, y, _facing);

        // Модули одной протяжкой не ставим, чтобы не наплодить их случайно
        if (_tool != Tool.Module)
        {
            _mode = DragMode.Paint;
            _lastX = x; _lastY = y;
        }
    }

    void PaintDrag(Layout layout, int x, int y)
    {
        int cx = _lastX, cy = _lastY;
        int px = cx, py = cy;
        Dir d;

        while (StepToward(ref cx, ref cy, x, y, out d))
        {
            if (_tool == Tool.Belt)
            {
                // Предыдущая лента поворачивается в сторону движения мыши
                Cell p = layout.Get(px, py);
                if (p.Kind == CellKind.Belt && p.Facing != d)
                    _world.Place(layout, px, py, CellKind.Belt, d);
                _facing = d;
            }

            if (layout.Get(cx, cy).Kind != CellKind.Module)
                PlaceCell(layout, cx, cy, _facing);

            px = cx; py = cy;
        }
        _lastX = cx; _lastY = cy;
    }

    void EraseDrag(Event e, Layout layout, int x, int y)
    {
        int cx = _lastX, cy = _lastY;
        Dir d;
        while (StepToward(ref cx, ref cy, x, y, out d))
            EraseCell(e, layout, cx, cy, false);
        _lastX = cx; _lastY = cy;
    }

    void PlaceCell(Layout layout, int x, int y, Dir facing)
    {
        Cell cur = layout.Get(x, y);
        if (cur.Kind == CellKind.Module) return;

        CellKind kind = ToKind(_tool);
        if (cur.Kind == kind && cur.Facing == facing) return;

        PlaceResult r = _world.Build(layout, x, y, kind, facing);   // с оплатой низшей эссенцией
        if (r == PlaceResult.OutputLimit)
            _status = $"Лимит выходов в модуле: {_world.MaxOutputs}. Больше откроется с прокачкой.";
        else if (r == PlaceResult.NotEnoughEssence)
        {
            PlaceCostInfo pc = Cost.PlaceCost(layout, x, y, kind);
            _status = $"Не хватает низшей эссенции: нужно {pc.Total}, есть {_world.Lower}.";
        }
        else if (r == PlaceResult.NotInRoot)
            _status = "Входы и выходы ставятся только внутри модуля.";
        else if (r == PlaceResult.WrongZone)
            _status = kind == CellKind.Extractor
                ? "Экстрактор ставится только на область высокой плотности (синяя)."
                : "Флуктуатор ставится только на дестабилизированную область (оранжевая).";
    }

    void EraseCell(Event e, Layout layout, int x, int y, bool initial)
    {
        Cell cur = layout.Get(x, y);
        if (cur.Kind == CellKind.Empty) return;

        if (cur.Kind == CellKind.Module)
        {
            if (!initial) return;   // протяжкой модули не удаляем
            if (!e.shift) { _status = "Удалить модуль вместе с содержимым: Shift+ПКМ."; return; }
        }
        _world.Erase(layout, x, y);
    }

    // ---------- отрисовка ----------

    static string Arrow(Dir d) => d == Dir.N ? "^" : d == Dir.E ? ">" : d == Dir.S ? "v" : "<";

    static string CellText(Cell c)
    {
        switch (c.Kind)
        {
            case CellKind.Module: return "M" + c.Link;
            case CellKind.Input: return "I" + Arrow(c.Facing);
            case CellKind.Output: return "O" + Arrow(c.Facing);
            case CellKind.Source: return "S" + Arrow(c.Facing);
            case CellKind.Sink: return "X";
            case CellKind.Belt: return Arrow(c.Facing);
            case CellKind.Extractor: return "E" + Arrow(c.Facing);
            case CellKind.Stabilizer:
                return (c.Timer == StabilizerMode.Both ? "B" : c.Timer == StabilizerMode.Lower ? "L" : "H") + Arrow(c.Facing);
            case CellKind.Fluctuator: return "F";
            case CellKind.QuantumNode: return "N";
            default: return "";
        }
    }

    static Color KindColor(CellKind k)
    {
        switch (k)
        {
            case CellKind.Belt: return new Color(0.35f, 0.35f, 0.40f);
            case CellKind.Source: return new Color(0.20f, 0.60f, 0.30f);
            case CellKind.Sink: return new Color(0.70f, 0.25f, 0.25f);
            case CellKind.Module: return new Color(0.45f, 0.30f, 0.65f);
            case CellKind.Input: return new Color(0.15f, 0.55f, 0.60f);
            case CellKind.Output: return new Color(0.85f, 0.50f, 0.15f);
            case CellKind.Extractor: return new Color(0.25f, 0.50f, 0.85f);
            case CellKind.Stabilizer: return new Color(0.35f, 0.70f, 0.35f);
            case CellKind.Fluctuator: return new Color(0.95f, 0.35f, 0.25f);
            case CellKind.QuantumNode: return new Color(0.95f, 0.85f, 0.20f);
            default: return ChunkB;
        }
    }

    // Может ли клетка принять предмет, движущийся в направлении moveDir (для соединения лент)
    static bool AcceptsFrom(Cell n, Dir moveDir)
    {
        switch (n.Kind)
        {
            case CellKind.Belt:
            case CellKind.Stabilizer:
                return n.Facing != DirUtil.Opposite(moveDir);   // лоб в лоб не принимают
            case CellKind.Sink:
            case CellKind.Module:
            case CellKind.Output:
                return true;
            default:
                return false;
        }
    }

    // Отдаёт ли клетка предметы в направлении towards.
    // Модуль отдаёт туда, куда смотрит хотя бы один выход внутри него
    bool FeedsInto(Cell n, Dir towards)
    {
        switch (n.Kind)
        {
            case CellKind.Belt:
            case CellKind.Input:
            case CellKind.Source:
            case CellKind.Extractor:
            case CellKind.Stabilizer:
                return n.Facing == towards;
            case CellKind.Module:
            {
                Layout child = _world.Get(n.Link);
                return child != null && child.HasOutputFacing(towards);
            }
            default:
                return false;
        }
    }

    // Какие стороны клетки ленты соединены с соседями (бит 1 << (int)Dir): вперёд, если там принимающая клетка,
    // назад и вбок, если оттуда в нас подают
    int BeltMask(Layout layout, int x, int y, Cell c)
    {
        int mask = 0;
        for (int d = 0; d < 4; d++)
        {
            Dir dir = (Dir)d;
            Cell n = layout.Get(x + DirUtil.Dx(dir), y + DirUtil.Dy(dir));
            if (n.Kind == CellKind.Empty) continue;

            bool linked = dir == c.Facing
                ? AcceptsFrom(n, dir)
                : FeedsInto(n, DirUtil.Opposite(dir));
            if (linked) mask |= 1 << d;
        }
        return mask;
    }

    static Rect HudRect(Rect view)
    {
        const float w = 270f;
        return new Rect(view.width - w - 10f, 10f, w, 92f);
    }

    // Плашка в углу вида: запас эссенций и цена блока под курсором
    void DrawHud(Layout layout, Rect view, bool hover, int hx, int hy)
    {
        Rect box = HudRect(view);

        Visuals.FillRound(box, new Color(0.04f, 0.05f, 0.09f, 0.85f), 10f);
        Visuals.StrokeRound(box, new Color(0.35f, 0.50f, 1f, 0.45f), 1.5f, 10f);
        Visuals.DrawItem(new Vector2(box.x + 16f, box.y + 18f), 14f, ItemColor(Items.HigherEssence));
        Visuals.DrawItem(new Vector2(box.x + 16f, box.y + 42f), 14f, ItemColor(Items.LowerEssence));

        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 28f, box.y + 6f, box.width - 34f, 22f), $"Высшая эссенция: {_world.Higher}");
        GUI.Label(new Rect(box.x + 28f, box.y + 30f, box.width - 34f, 22f), $"Низшая эссенция: {_world.Lower}");

        string cost = "Постановка: наведите на клетку";
        bool affordable = true;
        if (hover && _tool != Tool.Enter)
        {
            PlaceCostInfo pc = Cost.PlaceCost(layout, hx, hy, ToKind(_tool));
            cost = $"Постановка: {pc.Total} (блок {pc.Block} + размер {pc.Size})";
            affordable = pc.Total <= _world.Lower;
        }
        GUI.color = affordable ? Color.white : new Color(1f, 0.45f, 0.40f);
        GUI.Label(new Rect(box.x + 10f, box.y + 60f, box.width - 20f, 24f), cost);
        GUI.color = Color.white;
    }

    static Color ZoneColor(ZoneKind z)
    {
        switch (z)
        {
            case ZoneKind.HighDensity: return new Color(0.25f, 0.55f, 1.00f, 0.30f);
            case ZoneKind.Destabilized: return new Color(1.00f, 0.45f, 0.15f, 0.30f);
            case ZoneKind.Compressed: return new Color(0.75f, 0.30f, 1.00f, 0.32f);
            default: return Color.clear;
        }
    }

    static string ZoneName(ZoneKind z)
    {
        switch (z)
        {
            case ZoneKind.HighDensity: return "высокая плотность";
            case ZoneKind.Destabilized: return "дестабилизированная";
            case ZoneKind.Compressed: return "сжатое пространство";
            default: return "-";
        }
    }

    static Color ItemColor(byte item)
    {
        switch (item)
        {
            case Items.UnstableEssence: return new Color(0.90f, 0.30f, 1.00f);
            case Items.HigherEssence: return new Color(1.00f, 0.97f, 0.70f);
            case Items.LowerEssence: return new Color(0.40f, 1.00f, 0.50f);
            default: return Color.yellow;
        }
    }

    // Можно ли поставить этот блок сюда (для красной подсветки призрака)
    static bool CanPlace(Layout layout, int x, int y, CellKind kind)
    {
        if (kind == CellKind.Extractor) return layout.ZoneAt(x, y) == ZoneKind.HighDensity;
        if (kind == CellKind.Fluctuator) return layout.ZoneAt(x, y) == ZoneKind.Destabilized;
        if (kind == CellKind.Input || kind == CellKind.Output) return layout.ParentId >= 0;
        return true;
    }

    // Линия произвольного наклона: прямоугольник, повёрнутый вокруг начальной точки
    static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1f) return;

        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }

    static string CellInfo(Cell c)
    {
        string s = "";
        switch (c.Kind)
        {
            case CellKind.Module: s = c.Powered != 0 ? "модуль: питание есть" : "модуль: нет питания"; break;
            case CellKind.Extractor: s = c.Powered != 0 ? "экстрактор: работает" : "экстрактор: нет питания"; break;
            case CellKind.Stabilizer: s = "стабилизатор: " + StabilizerMode.Name(c.Timer); break;
            case CellKind.Fluctuator: s = "флуктуатор"; break;
            case CellKind.QuantumNode: s = "квантовый узел"; break;
        }
        if (c.Item != 0) s += (s.Length > 0 ? ", " : "") + "предмет: " + Items.Name(c.Item);
        return s.Length > 0 ? "[" + s + "]" : "";
    }

    string EnergyLine(Layout layout)
    {
        string s = $"Энергия слоя: узлов {layout.NodeCount}, флуктуаторов {layout.ProducerCount} (по {Energy.FluctuatorOutput}), " +
                   $"потребителей {layout.ConsumerCount}, радиус узла {Energy.NodeRadius}";

        if (layout.ParentId < 0) return s + $".   Сложность слоя {Energy.Complexity(layout)}.";

        Layout parent = _world.Get(layout.ParentId);
        string state = layout.Frozen ? (parent != null && parent.Frozen ? "ВЫКЛ (выше)" : "ВЫКЛ") : "ВКЛ";
        return s + $".   Этот модуль: {state}, сложность {Energy.Complexity(layout)}, расход {layout.EnergyDemand}/такт.";
    }

    void EnsureStyles()
    {
        Visuals.ApplySkin();
        if (_cellStyle != null) return;
        _cellStyle = new GUIStyle(GUI.skin.label);
        _cellStyle.alignment = TextAnchor.MiddleCenter;
        _cellStyle.fontStyle = FontStyle.Bold;
        _cellStyle.clipping = TextClipping.Overflow;
        _cellStyle.normal.textColor = Color.white;
    }

    void DrawView(Layout layout, Frame f, Rect view, bool hover, int hx, int hy)
    {
        GUI.BeginClip(view);   // view стоит в (0,0), поэтому координаты те же

        float time = Time.time;
        int depth = _stack.Count - 1;
        float frac = _paused ? 0f : Mathf.Clamp01(_acc / TickSeconds);   // доля текущего такта: для плавного движения предметов

        float halfW = view.width * 0.5f / f.Zoom;
        float halfH = view.height * 0.5f / f.Zoom;
        int minX = Mathf.FloorToInt(f.CamX - halfW) - 1;
        int maxX = Mathf.FloorToInt(f.CamX + halfW) + 1;
        int minY = Mathf.FloorToInt(f.CamY - halfH) - 1;
        int maxY = Mathf.FloorToInt(f.CamY + halfH) + 1;
        int minCx = minX >> Chunk.Shift, maxCx = maxX >> Chunk.Shift;
        int minCy = minY >> Chunk.Shift, maxCy = maxY >> Chunk.Shift;

        // Фон: оттенок зависит от глубины, чанки слегка различаются, на них мерцают звёзды
        GUI.color = Visuals.BackgroundColor(depth);
        GUI.DrawTexture(new Rect(0f, 0f, view.width, view.height), Texture2D.whiteTexture);

        for (int cy = minCy; cy <= maxCy; cy++)
        for (int cx = minCx; cx <= maxCx; cx++)
        {
            Rect a = CellRect(f, view, cx << Chunk.Shift, (cy << Chunk.Shift) + Chunk.Mask);
            Rect b = CellRect(f, view, (cx << Chunk.Shift) + Chunk.Mask, cy << Chunk.Shift);
            var chunkRect = new Rect(a.x, a.y, b.xMax - a.x, b.yMax - a.y);

            if (((cx + cy) & 1) != 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.022f);
                GUI.DrawTexture(chunkRect, Texture2D.whiteTexture);
            }
            if (f.Zoom >= 10f) Visuals.Stars(chunkRect, cx, cy, depth, time);
        }

        // Зоны пространства (пятно целиком лежит в своём чанке)
        bool zoneGlyphs = f.Zoom >= 18f;
        for (int cy = minCy; cy <= maxCy; cy++)
        for (int cx = minCx; cx <= maxCx; cx++)
        {
            int zx, zy, zr;
            ZoneKind z = ZoneGenerator.PatchOf(layout.Seed, layout.CompressedAllowed, cx, cy, out zx, out zy, out zr);
            if (z == ZoneKind.None) continue;

            for (int dy = -zr; dy <= zr; dy++)
            for (int dx = -zr; dx <= zr; dx++)
            {
                int d2 = dx * dx + dy * dy;
                if (d2 > zr * zr) continue;

                bool rim = d2 > (zr - 1) * (zr - 1);
                Visuals.ZoneCell(z, CellRect(f, view, zx + dx, zy + dy), f.Zoom, time, zx + dx, zy + dy,
                    d2 / (float)(zr * zr), rim, zoneGlyphs);
            }
        }

        // Линии сетки, границы чанков ярче
        bool detail = f.Zoom >= 20f;
        if (detail)
        {
            for (int x = minX; x <= maxX + 1; x++)
            {
                Rect r = CellRect(f, view, x, 0);
                GUI.color = (x & Chunk.Mask) == 0 ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0.04f);
                GUI.DrawTexture(new Rect(r.x, 0, 1, view.height), Texture2D.whiteTexture);
            }
            for (int y = minY; y <= maxY + 1; y++)
            {
                Rect r = CellRect(f, view, 0, y);   // верхняя граница строки y
                GUI.color = ((y + 1) & Chunk.Mask) == 0 ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0.04f);
                GUI.DrawTexture(new Rect(0, r.y, view.width, 1), Texture2D.whiteTexture);
            }
        }

        bool showText = f.Zoom >= 30f;
        if (showText) _cellStyle.fontSize = Mathf.RoundToInt(f.Zoom * 0.30f);

        var chunks = layout.Chunks;

        // Клетки: проходим только по существующим чанкам
        for (int n = 0; n < chunks.Count; n++)
        {
            Chunk ch = chunks[n];
            if (ch.Cx < minCx || ch.Cx > maxCx || ch.Cy < minCy || ch.Cy > maxCy) continue;

            int baseX = ch.Cx << Chunk.Shift;
            int baseY = ch.Cy << Chunk.Shift;
            for (int i = 0; i < Chunk.CellCount; i++)
            {
                Cell c = ch.Cells[i];
                if (c.Kind == CellKind.Empty) continue;

                int wx = baseX + (i & Chunk.Mask);
                int wy = baseY + (i >> Chunk.Shift);
                if (wx < minX || wx > maxX || wy < minY || wy > maxY) continue;

                Rect r = CellRect(f, view, wx, wy);
                if (c.Kind == CellKind.Belt) Visuals.BeltEdge(r, f.Zoom, BeltMask(layout, wx, wy, c));   // заливка во втором проходе
                else Visuals.DrawCell(c, r, f.Zoom, time, wx, wy);

                // Номер вложенного слоя на модуле
                if (showText && c.Kind == CellKind.Module)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.9f);
                    GUI.Label(new Rect(r.x, r.yMax - f.Zoom * 0.36f, r.width, f.Zoom * 0.32f), "M" + c.Link, _cellStyle);
                }
            }
        }

        // Ленты: заливка дорожек поверх всех рамок, чтобы соседние ленты выглядели цельно
        for (int n = 0; n < chunks.Count; n++)
        {
            Chunk ch = chunks[n];
            if (ch.Cx < minCx || ch.Cx > maxCx || ch.Cy < minCy || ch.Cy > maxCy) continue;

            int baseX = ch.Cx << Chunk.Shift;
            int baseY = ch.Cy << Chunk.Shift;
            for (int i = 0; i < Chunk.CellCount; i++)
            {
                Cell c = ch.Cells[i];
                if (c.Kind != CellKind.Belt) continue;

                int wx = baseX + (i & Chunk.Mask);
                int wy = baseY + (i >> Chunk.Shift);
                if (wx < minX || wx > maxX || wy < minY || wy > maxY) continue;

                Visuals.BeltBody(c, CellRect(f, view, wx, wy), f.Zoom, time, BeltMask(layout, wx, wy, c));
            }
        }

        // Предметы рисуем отдельным проходом, чтобы соседние клетки не закрывали их
        for (int n = 0; n < chunks.Count; n++)
        {
            Chunk ch = chunks[n];
            if (ch.Cx < minCx || ch.Cx > maxCx || ch.Cy < minCy || ch.Cy > maxCy) continue;

            int baseX = ch.Cx << Chunk.Shift;
            int baseY = ch.Cy << Chunk.Shift;
            for (int i = 0; i < Chunk.CellCount; i++)
            {
                Cell c = ch.Cells[i];
                if (c.Item == 0) continue;

                int wx = baseX + (i & Chunk.Mask);
                int wy = baseY + (i >> Chunk.Shift);
                if (wx < minX || wx > maxX || wy < minY || wy > maxY) continue;

                Rect ir = CellRect(f, view, wx, wy);
                Vector2 pos = ir.center;
                if (c.Kind == CellKind.Belt || c.Kind == CellKind.Input)
                {
                    // Предмет едет по клетке от входа через центр к выходу за BeltPeriod тактов (на повороте огибает угол)
                    // Позиция между тактами: пока предмет не упёрся в конец клетки, добавляем долю такта.
                    // Ждущий в конце клетки предмет (Timer == BeltPeriod) стоит на месте
                    float tt = Mathf.Min(c.Timer, Simulation.BeltPeriod);
                    if (c.Timer < Simulation.BeltPeriod) tt += frac;
                    tt /= Simulation.BeltPeriod;
                    int mask = c.Kind == CellKind.Belt ? BeltMask(layout, wx, wy, c) : 0;
                    pos = Visuals.BeltPos(ir, f.Zoom, c.Facing, mask, tt);
                }
                Visuals.DrawItem(pos, Mathf.Max(4f, Mathf.Round(f.Zoom * 0.30f)), ItemColor(c.Item));
            }
        }

        // Энергосеть: радиусы узлов и связи с бегущими огоньками
        float linkWidth = Mathf.Max(2f, f.Zoom * 0.07f);
        for (int nd = 0; nd < layout.NodeCount; nd++)
        {
            int nx, ny;
            layout.NodeAt(nd, out nx, out ny);
            Vector2 origin = CellRect(f, view, nx, ny).center;

            bool focus = _tool == Tool.QuantumNode || (hover && hx == nx && hy == ny);
            Visuals.RadiusRing(origin, Energy.NodeRadius * f.Zoom, focus ? 0.38f : 0.10f);

            for (int k = 0; k < layout.ProducerCount; k++)
            {
                int ex, ey;
                layout.ProducerAt(k, out ex, out ey);
                if (!EnergyNetwork.InRange(nx, ny, ex, ey)) continue;

                // Энергия течёт от флуктуатора к узлу
                Visuals.Link(CellRect(f, view, ex, ey).center, origin, linkWidth, true, time,
                    nx * 0.37f + ny * 0.29f + ex * 0.53f + ey * 0.41f);
            }
            for (int k = 0; k < layout.ConsumerCount; k++)
            {
                int ex, ey;
                layout.ConsumerAt(k, out ex, out ey);
                if (!EnergyNetwork.InRange(nx, ny, ex, ey)) continue;

                // От узла к потребителю; без питания связь красная и без огоньков
                bool on = layout.Get(ex, ey).Powered != 0;
                Visuals.Link(origin, CellRect(f, view, ex, ey).center, linkWidth, on, time,
                    nx * 0.37f + ny * 0.29f + ex * 0.53f + ey * 0.41f);
            }
        }

        // Призрак выбранного блока под курсором
        if (hover && _mode != DragMode.Pan)
        {
            Rect g = CellRect(f, view, hx, hy);

            if (_tool == Tool.Enter)
            {
                Visuals.Fill(g, new Color(1f, 1f, 1f, 0.16f));
            }
            else
            {
                CellKind gk = ToKind(_tool);
                bool bad = !CanPlace(layout, hx, hy, gk) || Cost.PlaceCost(layout, hx, hy, gk).Total > _world.Lower;

                Visuals.Alpha = 0.6f;
                int gmask = gk == CellKind.Belt
                    ? BeltMask(layout, hx, hy, new Cell { Kind = CellKind.Belt, Facing = _facing })
                    : 0;
                Visuals.DrawCell(new Cell { Kind = gk, Facing = _facing, Powered = 1 }, g, f.Zoom, time, hx, hy, gmask);
                Visuals.Alpha = 1f;

                if (bad) Visuals.Fill(g, new Color(0.95f, 0.15f, 0.15f, 0.45f));
                if (gk == CellKind.QuantumNode) Visuals.RadiusRing(g.center, Energy.NodeRadius * f.Zoom, 0.38f);
            }

            Visuals.Tex(Visuals.Frame, g, new Color(1f, 1f, 1f, 0.55f));
        }

        GUI.color = Color.white;
        GUI.EndClip();
    }

    void DrawPanel(Layout layout, Frame f, Rect view, bool hover, int hx, int hy)
    {
        float w = Screen.width;
        GUI.color = new Color(0.06f, 0.07f, 0.11f, 1f);
        GUI.DrawTexture(new Rect(0, view.height, w, PanelH), Texture2D.whiteTexture);
        GUI.color = new Color(0.35f, 0.50f, 1f, 0.55f);
        GUI.DrawTexture(new Rect(0, view.height, w, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = Pad;
        float y = view.height + 6;
        float lw = w - Pad * 2;

        GUI.Label(new Rect(x, y, lw, 22),
            $"{BreadcrumbText()}    Глубина: {_stack.Count - 1}    Такт: {_world.Tick}    Слоёв: {_world.LayoutCount}{(_paused ? "    [ПАУЗА]" : "")}");

        string ports = layout.ParentId < 0
            ? "Корневой слой: входов и выходов нет."
            : $"Входов: {layout.InputCount}   Выходов: {layout.OutputCount} / {_world.MaxOutputs}";
        GUI.Label(new Rect(x, y + 20, lw, 22),
            $"{ports}    Чанков: {layout.ChunkCount}    Доставлено здесь: {layout.Delivered}");

        GUI.Label(new Rect(x, y + 40, lw, 22),
            $"Цена слоя: {_cost.Total}  (блоки {_cost.Blocks} + размер {_cost.Size} [{_cost.Width}x{_cost.Height}] + вложенные {_cost.Nested})");

        GUI.Label(new Rect(x, y + 60, lw, 22), EnergyLine(layout));

        string cur = hover
            ? $"({hx}, {hy})   зона: {ZoneName(layout.ZoneAt(hx, hy))}   {CellInfo(layout.Get(hx, hy))}"
            : "-";
        GUI.Label(new Rect(x, y + 80, lw, 22),
            $"Курсор: {cur}    Направление: {Arrow(_facing)}    Масштаб: {f.Zoom:0} px/клетка");

        _tool = (Tool)GUI.Toolbar(new Rect(x, y + 106, Mathf.Min(960f, lw), 28), (int)_tool, ToolNames);

        float row = y + 140;
        if (GUI.Button(new Rect(x, row, 80, 28), _paused ? "Старт" : "Пауза")) _paused = !_paused;
        if (GUI.Button(new Rect(x + 85, row, 60, 28), "Шаг")) _world.Step();
        if (GUI.Button(new Rect(x + 150, row, 70, 28), "Наружу")) Exit();
        if (GUI.Button(new Rect(x + 225, row, 90, 28), "К центру")) FocusContent(f, layout);
        if (GUI.Button(new Rect(x + 320, row, 90, 28), "+1 выход")) _world.MaxOutputs++;
        if (GUI.Button(new Rect(x + 415, row, 70, 28), "Сброс")) { Reset(); return; }
        if (GUI.Button(new Rect(x + 490, row, 170, 28), "Проверка детерминизма"))
            _status = DeterminismCheck() ? "Детерминизм: OK" : "Детерминизм: ОШИБКА";
        if (GUI.Button(new Rect(x + 665, row, 100, 28), "+100 низш.")) _world.AddEssence(0, 100);
        if (GUI.Button(new Rect(x + 770, row, 100, 28), "+100 высш.")) _world.AddEssence(100, 0);

        GUI.Label(new Rect(x, row + 32, lw, 22), _status);
        GUI.Label(new Rect(x, row + 52, lw, 72),
            "ЛКМ: ставить (тяните - лента поворачивается сама)   ПКМ: стирать   R / Shift+R: поворот   Колесо: зум   " +
            "СКМ или Пробел+ЛКМ: двигать вид   WASD: камера   F: к центру   E / двойной клик: войти   Esc / Q: выйти   " +
            "P: пауза   Инструменты: 1-9, 0, -   Экстрактор только на синей зоне, флуктуатор только на оранжевой. " +
            "Стабилизатор: клик по нему (инструмент 9 или 7) меняет режим. " +
            "Строительство тратит низшую эссенцию; приёмник собирает высшую и низшую эссенции в запас (правый верхний угол).");
    }

    void OnGUI()
    {
        if (_world == null) return;
        EnsureStyles();
        Event e = Event.current;
        Rect view = ViewRect();

        HandleInput(e, view);

        // Ввод мог сменить слой или сбросить мир, берём актуальные
        if (_world == null || _stack.Count == 0) return;
        Frame f = Top;
        Layout layout = _world.Get(f.LayoutId);
        if (layout == null) { Reset(); return; }

        bool hover = view.Contains(e.mousePosition);
        int hx = 0, hy = 0;
        if (hover) ScreenToCell(f, view, e.mousePosition, out hx, out hy);

        if (e.type == EventType.Repaint)
        {
            _cost = Cost.Of(_world, layout);   // пока без кэша, для прототипа достаточно
            DrawView(layout, f, view, hover, hx, hy);
        }

        DrawHud(layout, view, hover, hx, hy);
        DrawPanel(layout, f, view, hover, hx, hy);
    }
}