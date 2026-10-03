using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VaultCapture.App;

/// <summary>Color palette for the light and dark themes.</summary>
internal sealed class Palette
{
    public bool IsDark;
    public Color Window, Surface, SurfaceHover, Border, Text, Muted, Accent, AccentHover, AccentText, Success, Selection, Danger;

    public static readonly Palette Light = new Palette
    {
        IsDark = false,
        Window = Color.FromArgb(255, 255, 255),
        Surface = Color.FromArgb(246, 246, 249),
        SurfaceHover = Color.FromArgb(236, 236, 242),
        Border = Color.FromArgb(214, 214, 224),
        Text = Color.FromArgb(28, 28, 32),
        Muted = Color.FromArgb(110, 110, 122),
        Accent = Color.FromArgb(0, 122, 255),
        AccentHover = Color.FromArgb(0, 104, 222),
        AccentText = Color.White,
        Success = Color.FromArgb(22, 128, 61),
        Selection = Color.FromArgb(224, 236, 255),
        Danger = Color.FromArgb(200, 40, 40),
    };

    public static readonly Palette Dark = new Palette
    {
        IsDark = true,
        Window = Color.FromArgb(32, 32, 32),
        Surface = Color.FromArgb(44, 44, 46),
        SurfaceHover = Color.FromArgb(58, 58, 60),
        Border = Color.FromArgb(62, 62, 64),
        Text = Color.FromArgb(240, 240, 242),
        Muted = Color.FromArgb(152, 152, 158),
        Accent = Color.FromArgb(10, 132, 255),
        AccentHover = Color.FromArgb(0, 113, 227),
        AccentText = Color.White,
        Success = Color.FromArgb(48, 209, 88),
        Selection = Color.FromArgb(28, 64, 110),
        Danger = Color.FromArgb(248, 113, 113),
    };
}

internal static class Theme
{
    public static Palette Current { get; private set; } = Palette.Light;
    public static event Action Changed;

    /// <summary>mode: "System", "Light" or "Dark".</summary>
    public static void Apply(string mode)
    {
        bool dark = string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase) ||
                    (!string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase) && IsSystemDark());
        var p = dark ? Palette.Dark : Palette.Light;
        if (p == Current) return;
        Current = p;
        Changed?.Invoke();
    }

    public static bool IsSystemDark()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    // ---- native dark-mode helpers (no-ops on systems that don't support them) ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    /// <summary>Dark/light window title bar (Windows 10 2004+ / Windows 11).</summary>
    public static void TitleBar(Form f)
    {
        if (!f.IsHandleCreated) return;
        try
        {
            int v = Current.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
        }
        catch { }
    }

    /// <summary>Dark scrollbars / combo boxes for native controls.</summary>
    public static void NativeControl(Control c, string darkClass = "DarkMode_Explorer")
    {
        if (!c.IsHandleCreated) return;
        try { SetWindowTheme(c.Handle, Current.IsDark ? darkClass : "Explorer", null); } catch { }
    }

    // ---- drawing helpers ----

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Recursively styles standard WinForms controls with the current palette.</summary>
    public static void StyleTree(Control root)
    {
        var p = Current;
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case ThemedControl _:
                    c.Invalidate();
                    break;
                case TextBox tb:
                    tb.BackColor = p.Surface; tb.ForeColor = p.Text;
                    NativeControl(tb);
                    break;
                case ComboBox cb:
                    cb.BackColor = p.Surface; cb.ForeColor = p.Text; cb.FlatStyle = FlatStyle.Flat;
                    NativeControl(cb, "DarkMode_CFD");
                    break;
                case Button b:
                    if (!(b.Tag is string t && t == "accent"))
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.BackColor = p.Surface; b.ForeColor = p.Text;
                        b.FlatAppearance.BorderColor = p.Border;
                        b.FlatAppearance.MouseOverBackColor = p.SurfaceHover;
                        b.FlatAppearance.MouseDownBackColor = p.Border;
                    }
                    else
                    {
                        b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
                        b.BackColor = p.Accent; b.ForeColor = p.AccentText;
                        b.FlatAppearance.MouseOverBackColor = p.AccentHover;
                        b.FlatAppearance.MouseDownBackColor = p.AccentHover;
                    }
                    break;
                case CheckBox ch:
                    ch.FlatStyle = FlatStyle.Flat; ch.ForeColor = p.Text; ch.BackColor = Color.Transparent;
                    ch.FlatAppearance.BorderColor = p.Muted; ch.FlatAppearance.CheckedBackColor = p.Surface;
                    break;
                case RadioButton rb:
                    rb.FlatStyle = FlatStyle.Flat; rb.ForeColor = p.Text; rb.BackColor = Color.Transparent;
                    rb.FlatAppearance.BorderColor = p.Muted; rb.FlatAppearance.CheckedBackColor = p.Surface;
                    break;
                case DataGridView g:
                    g.EnableHeadersVisualStyles = false;
                    g.BackgroundColor = p.Window;
                    g.GridColor = p.Border;
                    g.BorderStyle = BorderStyle.FixedSingle;
                    g.DefaultCellStyle.BackColor = p.Surface;
                    g.DefaultCellStyle.ForeColor = p.Text;
                    g.DefaultCellStyle.SelectionBackColor = p.Selection;
                    g.DefaultCellStyle.SelectionForeColor = p.Text;
                    g.ColumnHeadersDefaultCellStyle.BackColor = p.Window;
                    g.ColumnHeadersDefaultCellStyle.ForeColor = p.Muted;
                    g.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Window;
                    g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                    g.RowHeadersDefaultCellStyle.BackColor = p.Window;
                    g.RowHeadersDefaultCellStyle.SelectionBackColor = p.Selection;
                    g.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                    NativeControl(g);
                    break;
                case Label l:
                    if (!(l.Tag is string lt && lt == "muted")) l.ForeColor = p.Text; else l.ForeColor = p.Muted;
                    l.BackColor = Color.Transparent;
                    break;
                case Panel _:
                    if (!(c.Tag is string pt && pt == "keepcolor")) c.BackColor = Color.Transparent;
                    break;
            }
            if (!(c is ThemedControl) && !(c is DataGridView)) StyleTree(c);
        }
    }
}

/// <summary>Base for owner-drawn controls that repaint themselves from <see cref="Theme.Current"/>.</summary>
internal abstract class ThemedControl : Control
{
    protected bool Hover;

    protected ThemedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    /// <summary>Paints the rounded "pill" background used by inputs.</summary>
    protected void PaintPill(Graphics g, bool emphasize = false)
    {
        var p = Theme.Current;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(Parent?.BackColor ?? p.Window)) g.FillRectangle(back, ClientRectangle);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(r, (int)(6 * DeviceScale)))
        using (var fill = new SolidBrush(Hover || emphasize ? p.SurfaceHover : p.Surface))
        using (var pen = new Pen(Focused ? p.Accent : p.Border, Focused ? 1.5f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
    }

    protected float DeviceScale
    {
        get { using (var g = CreateGraphics()) return g.DpiX / 96f; }
    }
}

/// <summary>Menu renderer that matches the current theme (used for the tray menu and destination menu).</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Current.Text : Theme.Current.Muted;
        if (e.Item is ToolStripMenuItem mi && e.Text == mi.ShortcutKeyDisplayString) e.TextColor = Theme.Current.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Current.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using (var pen = new Pen(Theme.Current.Accent, 2f))
            g.DrawLines(pen, new[] { new Point(r.Left + 3, r.Top + r.Height / 2), new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4), new Point(r.Right - 3, r.Top + 4) });
    }

    private sealed class Colors : ProfessionalColorTable
    {
        private static Palette P => Theme.Current;
        public override Color ToolStripDropDownBackground => P.Window;
        public override Color ImageMarginGradientBegin => P.Window;
        public override Color ImageMarginGradientMiddle => P.Window;
        public override Color ImageMarginGradientEnd => P.Window;
        public override Color MenuBorder => P.Border;
        public override Color MenuItemBorder => P.Selection;
        public override Color MenuItemSelected => P.Selection;
        public override Color MenuItemSelectedGradientBegin => P.Selection;
        public override Color MenuItemSelectedGradientEnd => P.Selection;
        public override Color MenuItemPressedGradientBegin => P.Selection;
        public override Color MenuItemPressedGradientEnd => P.Selection;
        public override Color SeparatorDark => P.Border;
        public override Color SeparatorLight => P.Border;
        public override Color CheckBackground => P.Window;
        public override Color CheckSelectedBackground => P.Selection;
        public override Color CheckPressedBackground => P.Selection;
    }
}
