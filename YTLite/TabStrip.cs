namespace YTLite;

/// <summary>
/// Owner-drawn tab strip. Click selects, drag reorders, × or middle-click closes, + opens a new tab.
/// The tab list is shared with MainForm, so reordering here changes the Ctrl+Tab order too.
/// </summary>
sealed class TabStrip : Control
{
    const int MaxTabWidth = 220, MinTabWidth = 70, PlusWidth = 32;

    readonly List<BrowserTab> _tabs;
    readonly ToolTip _tip = new();
    string _lastTip = "";
    Font? _plusFont;

    // Drag state: _pressed is the tab under a held left button; it becomes a drag once the
    // mouse moves past the system drag threshold. _dragX is the dragged tab's left edge.
    BrowserTab? _pressed;
    bool _dragging;
    int _pressX, _grabOffset, _dragX;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public BrowserTab? Selected { get; set; }
    public event EventHandler<BrowserTab>? TabClicked;
    public event EventHandler<BrowserTab>? CloseClicked;
    public event EventHandler? NewTabClicked;

    public TabStrip(List<BrowserTab> tabs)
    {
        _tabs = tabs;
        Dock = DockStyle.Top;
        Height = 32;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    int TabWidth => _tabs.Count == 0 ? MaxTabWidth
        : Math.Clamp((Width - PlusWidth - 8) / _tabs.Count, MinTabWidth, MaxTabWidth);

    Rectangle TabRect(int i) => new(4 + i * TabWidth, 3, TabWidth - 2, Height - 3);
    static Rectangle CloseRect(Rectangle r) => new(r.Right - 22, r.Top + (r.Height - 18) / 2, 18, 18);
    Rectangle PlusRect => new(4 + _tabs.Count * TabWidth, 3, PlusWidth, Height - 3);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(SystemColors.Control);

        for (int i = 0; i < _tabs.Count; i++)
            if (!(_dragging && _tabs[i] == _pressed))
                DrawTab(g, _tabs[i], TabRect(i));

        _plusFont ??= new Font(Font.FontFamily, Font.Size + 3);
        TextRenderer.DrawText(g, "+", _plusFont, PlusRect, SystemColors.ControlText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // The dragged tab follows the mouse, drawn last so it sits on top of its neighbours.
        if (_dragging && _pressed is { } dragged)
        {
            var r = TabRect(0);
            r.X = _dragX;
            DrawTab(g, dragged, r);
            using var edge = new Pen(SystemColors.ControlDark);   // side edges lift it off its neighbours
            g.DrawLine(edge, r.Left, r.Top, r.Left, r.Bottom);
            g.DrawLine(edge, r.Right - 1, r.Top, r.Right - 1, r.Bottom);
        }
    }

    void DrawTab(Graphics g, BrowserTab t, Rectangle r)
    {
        bool sel = t == Selected;
        using (var bg = new SolidBrush(sel ? SystemColors.Window : SystemColors.ControlLight))
            g.FillRectangle(bg, r);
        if (sel)
            using (var accent = new SolidBrush(Color.FromArgb(220, 30, 30)))
                g.FillRectangle(accent, r.Left, r.Top, r.Width, 2);

        int x = r.Left + 6;
        if (t.Favicon is { } icon)
        {
            g.DrawImage(icon, x, r.Top + (r.Height - 16) / 2, 16, 16);
            x += 20;
        }

        string prefix = t.PlayingAudio ? "🔊 " : t.Suspended ? "💤 " : "";
        var textRect = new Rectangle(x, r.Top, r.Right - x - 24, r.Height);
        TextRenderer.DrawText(g, prefix + t.Title, Font, textRect, SystemColors.ControlText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "✕", Font, CloseRect(r), SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (PlusRect.Contains(e.Location) && e.Button == MouseButtons.Left)
        {
            NewTabClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            var r = TabRect(i);
            if (!r.Contains(e.Location)) continue;
            var t = _tabs[i];
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(r).Contains(e.Location)))
                CloseClicked?.Invoke(this, t);
            else if (e.Button == MouseButtons.Left)
            {
                TabClicked?.Invoke(this, t);
                Capture = true; // selecting focuses the WebView, which can take the mouse capture
                _pressed = t;
                _pressX = e.X;
                _grabOffset = e.X - r.Left;
            }
            return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // _pressed is cleared on mouse-up or lost capture, so it alone means "left button held".
        if (_pressed is { } t && _tabs.Contains(t))
        {
            if (!_dragging && Math.Abs(e.X - _pressX) >= SystemInformation.DragSize.Width)
                _dragging = true;
            if (_dragging)
            {
                int w = TabWidth;
                _dragX = Math.Clamp(e.X - _grabOffset, 4, 4 + (_tabs.Count - 1) * w);
                // Move the tab into the slot its centre is over.
                int target = Math.Clamp((_dragX - 4 + w / 2) / w, 0, _tabs.Count - 1);
                int from = _tabs.IndexOf(t);
                if (target != from)
                {
                    _tabs.RemoveAt(from);
                    _tabs.Insert(target, t);
                }
                Invalidate();
                return;
            }
        }

        string tip = "";
        for (int i = 0; i < _tabs.Count; i++)
            if (TabRect(i).Contains(e.Location)) { tip = _tabs[i].Title; break; }
        if (PlusRect.Contains(e.Location)) tip = "New tab (Ctrl+T)";
        if (tip != _lastTip) { _lastTip = tip; _tip.SetToolTip(this, tip); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) EndDrag();
    }

    void EndDrag()
    {
        bool wasDragging = _dragging;
        _pressed = null;
        _dragging = false;
        if (wasDragging) Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tip.Dispose(); _plusFont?.Dispose(); }
        base.Dispose(disposing);
    }
}
