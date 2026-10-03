using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace VaultCapture.App;

/// <summary>Segoe MDL2 Assets icon glyphs (built into Windows 10/11).</summary>
internal static class Glyphs
{
    public const string Calendar = "";
    public const string Attach = "";
    public const string Airplane = "";
    public const string Book = "";
    public const string Task = "";
    public const string Inbox = "";
    public const string Note = "";
    public const string Clear = "";

    public static Font Font(float size) => new Font("Segoe MDL2 Assets", size);
}

/// <summary>
/// Themed date input. Compact mode is a small icon button (with a short date caption once set);
/// otherwise it's a full pill. Click (or Space/F4) opens a calendar; Up/Down change the day; Delete clears.
/// </summary>
internal sealed class DatePill : ThemedControl
{
    private DateTime? _value = DateTime.Today;
    private ToolStripDropDown _popup;
    private readonly ToolTip _tip = new ToolTip();

    public bool AllowNone { get; set; }
    public bool Compact { get; set; }
    public string Glyph { get; set; } = Glyphs.Calendar;
    public string NoneText { get; set; } = "None";
    public string Prefix { get; set; } = "";
    public string ToolTipText { set => _tip.SetToolTip(this, value); }
    public event EventHandler ValueChanged;
    public event EventHandler PopupClosed;
    public bool PopupOpen => _popup != null && _popup.Visible;

    public DateTime? Value
    {
        get => _value;
        set
        {
            var v = value?.Date;
            if (!AllowNone && v == null) v = DateTime.Today;
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>In compact mode, a caption is shown only when it adds information.</summary>
    private string Caption
    {
        get
        {
            if (_value == null) return "";
            var d = _value.Value;
            if (!AllowNone && d == DateTime.Today) return "";
            if (d == DateTime.Today) return "Today";
            if (d == DateTime.Today.AddDays(1)) return "Tomorrow";
            if (d == DateTime.Today.AddDays(-1)) return "Yesterday";
            return d.Year == DateTime.Today.Year ? d.ToString("MMM d") : d.ToString("MMM d, yyyy");
        }
    }

    /// <summary>Width the compact control wants for its current caption.</summary>
    public int PreferredCompactWidth
    {
        get
        {
            float sc = DeviceScale;
            int w = (int)(30 * sc);
            var c = Caption;
            if (c.Length > 0) w += TextRenderer.MeasureText(c, Font).Width + (int)(2 * sc);
            return w;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        if (Compact) { PaintCompact(g, p); return; }

        PaintPill(g);
        int pad = (int)(8 * DeviceScale);
        string text = _value == null ? NoneText : _value.Value.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture);
        var rect = new Rectangle(pad, 0, Width - pad * 2, Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (Prefix.Length > 0)
        {
            TextRenderer.DrawText(g, Prefix, Font, rect, p.Muted, flags);
            int w = TextRenderer.MeasureText(g, Prefix, Font, rect.Size, flags).Width + (int)(4 * DeviceScale);
            rect = new Rectangle(rect.X + w, 0, rect.Width - w, Height);
        }
        TextRenderer.DrawText(g, text, Font, rect, _value == null ? p.Muted : p.Text, flags);
    }

    private void PaintCompact(Graphics g, Palette p)
    {
        float sc = DeviceScale;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Window);
        if (Hover || Focused || PopupOpen)
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(6 * sc)))
            using (var b = new SolidBrush(p.SurfaceHover)) g.FillPath(b, path);

        bool active = Caption.Length > 0;
        var col = active ? p.Accent : p.Muted;
        int iconW = (int)(28 * sc);
        using (var f = Glyphs.Font(10.5f))
            TextRenderer.DrawText(g, Glyph, f, new Rectangle(0, 0, iconW, Height), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (active)
            TextRenderer.DrawText(g, Caption, Font, new Rectangle(iconW - (int)(2 * sc), 0, Width - iconW, Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Focus();
        if (e.Button == MouseButtons.Right && AllowNone) { Value = null; return; }
        OpenPopup();
    }

    protected override bool IsInputKey(Keys k) =>
        k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var baseDate = _value ?? DateTime.Today;
        switch (e.KeyCode)
        {
            case Keys.Up: Value = _value == null ? DateTime.Today : baseDate.AddDays(1); e.Handled = true; break;
            case Keys.Down: Value = _value == null ? DateTime.Today : baseDate.AddDays(-1); e.Handled = true; break;
            case Keys.PageUp: Value = baseDate.AddMonths(1); e.Handled = true; break;
            case Keys.PageDown: Value = baseDate.AddMonths(-1); e.Handled = true; break;
            case Keys.Delete: case Keys.Back: if (AllowNone) Value = null; e.Handled = true; break;
            case Keys.Space: case Keys.F4: OpenPopup(); e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    public void OpenPopup()
    {
        if (PopupOpen) return;
        var cal = new CalendarGrid(_value ?? DateTime.Today, _value, AllowNone) { Font = Font };
        var host = new ToolStripControlHost(cal) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = cal.Size };
        _popup = new ToolStripDropDown { Padding = Padding.Empty, Renderer = new ThemedMenuRenderer(), DropShadowEnabled = true };
        _popup.Items.Add(host);
        cal.Picked += d => { _popup.Close(); Value = d; Focus(); };
        _popup.Closed += (o, e) => { Invalidate(); PopupClosed?.Invoke(this, EventArgs.Empty); };
        // Open above the control if there's no room below.
        var below = PointToScreen(new Point(0, Height + 2));
        var wa = Screen.FromControl(this).WorkingArea;
        var pt = below.Y + cal.Height > wa.Bottom ? new Point(0, -cal.Height - 2) : new Point(0, Height + 2);
        if (Compact) pt.X = Width - cal.Width;
        _popup.Show(this, pt);
        cal.Focus();
    }
}

/// <summary>Owner-drawn month calendar shown in the date popup.</summary>
internal sealed class CalendarGrid : Control
{
    public event Action<DateTime?> Picked;

    private DateTime _month;
    private DateTime _cursor;
    private readonly DateTime? _selected;
    private readonly bool _allowNone;
    private Point _mouse = new Point(-1, -1);
    private readonly float _s;
    private readonly List<(Rectangle r, string text, Func<DateTime?> value)> _quick = new List<(Rectangle, string, Func<DateTime?>)>();

    private int Cell => (int)(34 * _s);
    private int Head => (int)(36 * _s);
    private int Pad => (int)(8 * _s);

    public CalendarGrid(DateTime show, DateTime? selected, bool allowNone)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        using (var g = CreateGraphics()) _s = g.DpiX / 96f;
        _month = new DateTime(show.Year, show.Month, 1);
        _cursor = show.Date;
        _selected = selected;
        _allowNone = allowNone;
        Size = new Size(Pad * 2 + Cell * 7, Head + (int)(22 * _s) + Cell * 6 + (int)(40 * _s) + Pad);
    }

    private DateTime GridStart
    {
        get
        {
            int first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)_month.DayOfWeek - first + 7) % 7;
            return _month.AddDays(-offset);
        }
    }

    private Rectangle PrevBtn => new Rectangle(Pad, Pad / 2, Cell, Head - Pad / 2);
    private Rectangle NextBtn => new Rectangle(Width - Pad - Cell, Pad / 2, Cell, Head - Pad / 2);
    private Rectangle DayRect(int i) => new Rectangle(Pad + (i % 7) * Cell, Head + (int)(22 * _s) + (i / 7) * Cell, Cell, Cell);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Window);
        using (var border = new Pen(p.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        using (var bold = new Font(Font, FontStyle.Bold))
            TextRenderer.DrawText(g, _month.ToString("MMMM yyyy"), bold, new Rectangle(0, Pad / 2, Width, Head - Pad / 2), p.Text, center);
        foreach (var (r, t) in new[] { (PrevBtn, "‹"), (NextBtn, "›") })
        {
            if (r.Contains(_mouse)) using (var b = new SolidBrush(p.SurfaceHover)) using (var path = Theme.RoundRect(Shrink(r, 4), (int)(6 * _s))) g.FillPath(b, path);
            using (var big = new Font(Font.FontFamily, Font.Size * 1.5f)) TextRenderer.DrawText(g, t, big, r, p.Text, center);
        }

        var dtf = CultureInfo.CurrentCulture.DateTimeFormat;
        int first = (int)dtf.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            string n = dtf.AbbreviatedDayNames[(first + i) % 7];
            TextRenderer.DrawText(g, n.Substring(0, Math.Min(2, n.Length)), Font,
                new Rectangle(Pad + i * Cell, Head, Cell, (int)(22 * _s)), p.Muted, center);
        }

        var start = GridStart;
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            var r = DayRect(i);
            var inner = Shrink(r, 3);
            bool sel = _selected.HasValue && d == _selected.Value.Date;
            bool cur = d == _cursor && Focused;
            bool hov = r.Contains(_mouse);
            using (var path = Theme.RoundRect(inner, (int)(6 * _s)))
            {
                if (sel) using (var b = new SolidBrush(p.Accent)) g.FillPath(b, path);
                else if (hov || cur) using (var b = new SolidBrush(p.SurfaceHover)) g.FillPath(b, path);
                if (d == DateTime.Today && !sel) using (var pen = new Pen(p.Accent, 1.5f)) g.DrawPath(pen, path);
            }
            var col = sel ? p.AccentText : d.Month == _month.Month ? p.Text : p.Muted;
            TextRenderer.DrawText(g, d.Day.ToString(), Font, r, col, center);
        }

        // quick actions
        _quick.Clear();
        var labels = new List<(string, Func<DateTime?>)> { ("Today", () => DateTime.Today), ("Tomorrow", () => DateTime.Today.AddDays(1)) };
        if (_allowNone) labels.Add(("Clear", () => null));
        int y = DayRect(41).Bottom + (int)(6 * _s), h = (int)(28 * _s), x = Pad, gap = (int)(6 * _s);
        int w = (Width - Pad * 2 - gap * (labels.Count - 1)) / labels.Count;
        foreach (var (text, val) in labels)
        {
            var r = new Rectangle(x, y, w, h);
            _quick.Add((r, text, val));
            using (var path = Theme.RoundRect(r, (int)(6 * _s)))
            using (var b = new SolidBrush(r.Contains(_mouse) ? p.SurfaceHover : p.Surface))
            using (var pen = new Pen(p.Border))
            { g.FillPath(b, path); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, text, Font, r, p.Text, center);
            x += w + gap;
        }
    }

    private static Rectangle Shrink(Rectangle r, int by) => new Rectangle(r.X + by, r.Y + by, r.Width - by * 2, r.Height - by * 2);

    protected override void OnMouseMove(MouseEventArgs e) { _mouse = e.Location; Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _mouse = new Point(-1, -1); Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (PrevBtn.Contains(e.Location)) { _month = _month.AddMonths(-1); Invalidate(); return; }
        if (NextBtn.Contains(e.Location)) { _month = _month.AddMonths(1); Invalidate(); return; }
        foreach (var q in _quick) if (q.r.Contains(e.Location)) { Picked?.Invoke(q.value()); return; }
        for (int i = 0; i < 42; i++)
            if (DayRect(i).Contains(e.Location)) { Picked?.Invoke(GridStart.AddDays(i)); return; }
    }

    protected override bool IsInputKey(Keys k) =>
        k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || k == Keys.Enter || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int delta = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -7 : e.KeyCode == Keys.Down ? 7 : 0;
        if (delta != 0)
        {
            _cursor = _cursor.AddDays(delta);
            _month = new DateTime(_cursor.Year, _cursor.Month, 1);
            Invalidate();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Picked?.Invoke(_cursor); e.Handled = true; }
        else if (e.KeyCode == Keys.PageUp) { _month = _month.AddMonths(-1); _cursor = _cursor.AddMonths(-1); Invalidate(); }
        else if (e.KeyCode == Keys.PageDown) { _month = _month.AddMonths(1); _cursor = _cursor.AddMonths(1); Invalidate(); }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
}

/// <summary>Flat destination selector: icon, bold label and an up/down chevron. Opens a themed menu.</summary>
internal sealed class DestPicker : ThemedControl
{
    private List<Destination> _items = new List<Destination>();
    private int _index = -1;
    private ContextMenuStrip _menu;

    public event EventHandler SelectedIndexChanged;
    public event EventHandler MenuClosed;
    public bool MenuOpen => _menu != null && _menu.Visible;

    public List<Destination> Items
    {
        get => _items;
        set { _items = value ?? new List<Destination>(); if (_index >= _items.Count) _index = _items.Count - 1; Invalidate(); }
    }

    public int SelectedIndex
    {
        get => _index;
        set
        {
            if (value < -1 || value >= _items.Count || value == _index) return;
            _index = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Destination Selected => _index >= 0 && _index < _items.Count ? _items[_index] : null;

    public int PreferredWidthNow
    {
        get
        {
            var d = Selected;
            float sc = DeviceScale;
            if (d == null) return (int)(120 * sc);
            using (var bold = new Font(Font, FontStyle.Bold))
                return (int)(36 * sc) + TextRenderer.MeasureText(d.Label, bold).Width + (int)(22 * sc);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float sc = DeviceScale;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Window);
        if (Hover || Focused || MenuOpen)
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(7 * sc)))
            using (var b = new SolidBrush(p.SurfaceHover)) g.FillPath(b, path);

        var d = Selected;
        if (d == null) return;
        int pad = (int)(8 * sc), iconW = (int)(24 * sc);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        using (var f = Glyphs.Font(10.5f))
            TextRenderer.DrawText(g, d.Glyph, f, new Rectangle(pad, 0, iconW, Height), p.Text, flags);
        int textW;
        using (var bold = new Font(Font, FontStyle.Bold))
        {
            textW = TextRenderer.MeasureText(g, d.Label, bold, new Size(Width, Height), flags).Width;
            TextRenderer.DrawText(g, d.Label, bold, new Rectangle(pad + iconW, 0, Width - pad - iconW, Height), p.Text, flags | TextFormatFlags.EndEllipsis);
        }

        // up/down chevron
        float cx = Math.Min(Width - pad - 4 * sc, pad + iconW + textW + 9 * sc), cy = Height / 2f, s = 3.2f * sc;
        using (var pen = new Pen(p.Text, 1.4f * sc))
        {
            g.DrawLines(pen, new[] { new PointF(cx - s, cy - 1.5f * sc), new PointF(cx, cy - s - 1.5f * sc), new PointF(cx + s, cy - 1.5f * sc) });
            g.DrawLines(pen, new[] { new PointF(cx - s, cy + 1.5f * sc), new PointF(cx, cy + s + 1.5f * sc), new PointF(cx + s, cy + 1.5f * sc) });
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Focus();
        OpenMenu();
    }

    protected override bool IsInputKey(Keys k) => k == Keys.Up || k == Keys.Down || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down) { if (_index < _items.Count - 1) SelectedIndex = _index + 1; e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { if (_index > 0) SelectedIndex = _index - 1; e.Handled = true; }
        else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.F4) { OpenMenu(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    public void OpenMenu()
    {
        if (MenuOpen) return;
        _menu?.Dispose();
        float sc = DeviceScale;
        _menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(), ShowImageMargin = true, ShowCheckMargin = false, Font = Font };
        _menu.MinimumSize = new Size((int)(240 * sc), 0);
        ToolStripMenuItem current = null;
        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var d = _items[i];
            if (i > 0 && d.Mode != _items[i - 1].Mode) _menu.Items.Add(new ToolStripSeparator());
            var mi = new ToolStripMenuItem(d.Label)
            {
                Image = GlyphImage(d.Glyph),
                ShortcutKeyDisplayString = d.Shortcut.Length > 0 ? "Ctrl+" + d.Shortcut : null,
                Padding = new Padding(0, (int)(3 * sc), 0, (int)(3 * sc)),
            };
            if (i == _index) { mi.Font = new Font(Font, FontStyle.Bold); current = mi; }
            mi.Click += (o, e) => { SelectedIndex = idx; };
            _menu.Items.Add(mi);
        }
        _menu.Closed += (o, e) => { Invalidate(); MenuClosed?.Invoke(this, EventArgs.Empty); };
        // The footer sits at the bottom of the window, so open upward when there's no room below.
        var size = _menu.GetPreferredSize(Size.Empty);
        var wa = Screen.FromControl(this).WorkingArea;
        var below = PointToScreen(new Point(0, Height + 2));
        var pt = below.Y + size.Height > wa.Bottom ? new Point(0, -size.Height - 2) : new Point(0, Height + 2);
        _menu.Show(this, pt);
        current?.Select();
    }

    private Image GlyphImage(string glyph)
    {
        int sz = (int)(18 * DeviceScale);
        var bmp = new Bitmap(sz, sz);
        using (var g = Graphics.FromImage(bmp))
        using (var f = Glyphs.Font(10f))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(Theme.Current.Text))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(glyph, f, b, new RectangleF(0, 0, sz, sz), sf);
        }
        return bmp;
    }
}

/// <summary>Fully rounded button. Accent = filled blue; otherwise a neutral gray pill.</summary>
internal sealed class PillButton : ThemedControl
{
    public bool Accent { get; set; }
    private bool _down;

    public PillButton() { SetStyle(ControlStyles.StandardClick, true); }

    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Window);
        Color fill = Accent ? (Hover || _down ? p.AccentHover : p.Accent) : (Hover || _down ? p.Border : p.SurfaceHover);
        Color text = Accent ? p.AccentText : p.Text;
        using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
        using (var b = new SolidBrush(fill))
        {
            g.FillPath(b, path);
            if (Focused) using (var pen = new Pen(p.Accent, 1.5f)) g.DrawPath(pen, path);
        }
        using (var f = new Font(Font, Accent ? FontStyle.Bold : FontStyle.Regular))
            TextRenderer.DrawText(g, Text, f, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Small icon-only button (used for Attach).</summary>
internal sealed class IconButton : ThemedControl
{
    public string Glyph { get; set; } = Glyphs.Attach;
    public bool Active { get; set; }
    private readonly ToolTip _tip = new ToolTip();
    public string ToolTipText { set => _tip.SetToolTip(this, value); }

    public IconButton() { SetStyle(ControlStyles.StandardClick, true); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Window);
        if (Hover || Focused)
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(6 * DeviceScale)))
            using (var b = new SolidBrush(p.SurfaceHover)) g.FillPath(b, path);
        using (var f = Glyphs.Font(10.5f))
            TextRenderer.DrawText(g, Glyph, f, ClientRectangle, Active ? p.Accent : p.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>A text box wrapped in a rounded, themed border.</summary>
internal sealed class TextField : Panel
{
    public readonly TextBox Box = new TextBox { BorderStyle = BorderStyle.None };
    private readonly float _s;

    public TextField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        using (var g = CreateGraphics()) _s = g.DpiX / 96f;
        Controls.Add(Box);
        Box.GotFocus += (o, e) => Invalidate();
        Box.LostFocus += (o, e) => Invalidate();
        Box.FontChanged += (o, e) => PositionBox();
        Tag = "keepcolor";
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); PositionBox(); }

    public void PositionBox()
    {
        int px = (int)(10 * _s);
        if (Box.Multiline)
            Box.SetBounds(px, (int)(8 * _s), Width - px - (int)(4 * _s), Height - (int)(14 * _s));
        else
            Box.SetBounds(px, Math.Max(2, (Height - Box.PreferredHeight) / 2), Width - px * 2, Box.PreferredHeight);
    }

    public void ApplyTheme()
    {
        var p = Theme.Current;
        BackColor = p.Window;
        Box.BackColor = p.Surface;
        Box.ForeColor = p.Text;
        Theme.NativeControl(Box);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Window);
        using (var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(7 * _s)))
        using (var fill = new SolidBrush(p.Surface))
        using (var pen = new Pen(Box.Focused ? p.Accent : p.Border, Box.Focused ? 1.5f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
    }
}
