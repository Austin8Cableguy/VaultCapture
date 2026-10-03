using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VaultCapture.Core;

namespace VaultCapture.App;

internal sealed class Destination
{
    public string Key;
    public string Label;
    public string Glyph;
    public CaptureMode Mode;
    public JournalType Journal;
    public string Shortcut = "";
}

/// <summary>The floating quick-capture window.</summary>
internal sealed class CaptureForm : Form
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Action _saveSettings;
    private AppSettings _s;
    private readonly List<Destination> _dests = new List<Destination>();
    private readonly List<string> _files = new List<string>();
    private readonly float _scale;

    private readonly DestPicker _dest = new DestPicker();
    private readonly TextBox _txt = new TextBox { BorderStyle = BorderStyle.None, Multiline = true, WordWrap = true };
    private readonly Label _placeholder = new Label();
    private readonly DatePill _date = new DatePill { Compact = true, Glyph = Glyphs.Calendar, ToolTipText = "Daily note date" };
    private readonly DatePill _start = new DatePill { Compact = true, AllowNone = true, Glyph = Glyphs.Airplane, ToolTipText = "Start date (right-click to clear)" };
    private readonly DatePill _due = new DatePill { Compact = true, AllowNone = true, Glyph = Glyphs.Calendar, ToolTipText = "Due date (right-click to clear)" };
    private readonly IconButton _btnAttach = new IconButton { Glyph = Glyphs.Attach, ToolTipText = "Attach files (Ctrl+O) — or drop files on this window" };
    private readonly Label _lblFiles = new Label();
    private readonly PillButton _btnCancel = new PillButton { Text = "Cancel" };
    private readonly PillButton _btnSave = new PillButton { Text = "Save", Accent = true };
    private int _dividerY;
    private readonly Timer _hideTimer = new Timer { Interval = 120 };

    private bool _modalOpen, _saving;
    private bool PickerOpen => _dest.MenuOpen || _date.PopupOpen || _start.PopupOpen || _due.PopupOpen;

    public CaptureForm(Func<AppSettings> getSettings, Action saveSettings)
    {
        _getSettings = getSettings;
        _saveSettings = saveSettings;
        _s = getSettings();

        AutoScaleMode = AutoScaleMode.None;
        using (var g = CreateGraphics()) _scale = g.DpiX / 96f;

        Text = "VaultCapture";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9.75f);
        AllowDrop = true;
        DoubleBuffered = true;

        _dest.SelectedIndexChanged += (o, e) => { ApplyMode(); _txt.Focus(); };
        _dest.MenuClosed += (o, e) => { if (Visible) _txt.Focus(); };

        _txt.Font = new Font("Segoe UI", 12f);
        _txt.TextChanged += (o, e) => _placeholder.Visible = _txt.TextLength == 0;
        _placeholder.Font = _txt.Font;
        _placeholder.Tag = "muted";
        _placeholder.AutoSize = true;
        _placeholder.Cursor = Cursors.IBeam;
        _placeholder.Click += (o, e) => _txt.Focus();

        foreach (var d in new[] { _date, _start, _due })
        {
            d.ValueChanged += (o, e) => Layout2();
            d.PopupClosed += (o, e) => { if (Visible) _txt.Focus(); };
        }

        _start.Value = null;
        _due.Value = null;
        _btnAttach.Click += (o, e) => PickFiles();

        _lblFiles.AutoEllipsis = true;
        _lblFiles.TextAlign = ContentAlignment.MiddleLeft;
        _lblFiles.Cursor = Cursors.Hand;
        _lblFiles.Tag = "muted";
        _lblFiles.Font = new Font("Segoe UI", 8.75f);
        _lblFiles.Click += (o, e) => { if (_files.Count > 0) { _files.Clear(); UpdateFiles(); } };

        _btnCancel.Click += (o, e) => Hide();
        _btnSave.Click += (o, e) => DoSave();

        Controls.AddRange(new Control[] { _placeholder, _txt, _date, _start, _due, _btnAttach, _lblFiles, _dest, _btnCancel, _btnSave });
        _placeholder.BringToFront();

        MouseDown += DragWindow;

        DragEnter += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragDrop += (o, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            {
                _files.AddRange(dropped.Where(File.Exists));
                UpdateFiles();
            }
        };

        _hideTimer.Tick += (o, e) =>
        {
            _hideTimer.Stop();
            if (!_modalOpen && !PickerOpen && !ContainsFocus && ActiveForm != this) Hide();
        };
        Deactivate += (o, e) => { if (!_modalOpen && !PickerOpen && !_saving) _hideTimer.Start(); };

        Theme.Changed += ApplyTheme;
        ApplyTheme();
        RebuildDestinations();
        _dest.SelectedIndex = 0;
        Layout2();
    }

    private int S(int px) => (int)Math.Round(px * _scale);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
            cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW (no Alt+Tab entry)
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.TryRoundCorners(Handle);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (InvokeRequired) { BeginInvoke((Action)ApplyTheme); return; }
        var p = Theme.Current;
        BackColor = p.Window;
        ForeColor = p.Text;
        Theme.StyleTree(this);
        _txt.BackColor = p.Window;
        _txt.ForeColor = p.Text;
        _placeholder.BackColor = p.Window;
        Theme.NativeControl(_txt);
        Invalidate(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var p = new Pen(Theme.Current.Border))
        {
            e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            e.Graphics.DrawLine(p, 1, _dividerY, Width - 2, _dividerY);
        }
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
    }

    // ---------- destinations ----------

    private void RebuildDestinations()
    {
        string prevKey = Current?.Key;
        _dests.Clear();
        _dests.Add(new Destination { Key = "daily", Glyph = Glyphs.Note, Label = "Daily note › " + (_s.CapturedHeading ?? "Captured"), Mode = CaptureMode.Daily, Shortcut = "D" });
        foreach (var j in _s.JournalTypes ?? new List<JournalType>())
        {
            if (string.IsNullOrWhiteSpace(j.Label)) continue;
            _dests.Add(new Destination
            {
                Key = "journal:" + j.Label, Glyph = Glyphs.Book, Label = j.Label, Mode = CaptureMode.Journal, Journal = j,
                Shortcut = string.IsNullOrWhiteSpace(j.Shortcut) ? "" : j.Shortcut.Trim().Substring(0, 1).ToUpperInvariant(),
            });
        }
        _dests.Add(new Destination { Key = "task", Glyph = Glyphs.Task, Label = "Task", Mode = CaptureMode.Task, Shortcut = "T" });
        _dests.Add(new Destination { Key = "inbox", Glyph = Glyphs.Inbox, Label = "Inbox note", Mode = CaptureMode.Inbox, Shortcut = "I" });

        _dest.Items = new List<Destination>(_dests);
        int idx = prevKey == null ? -1 : _dests.FindIndex(d => d.Key == prevKey);
        if (idx >= 0 && idx != _dest.SelectedIndex) _dest.SelectedIndex = idx;
        else if (_dest.SelectedIndex >= _dests.Count || _dest.SelectedIndex < 0) _dest.SelectedIndex = 0;
    }

    private Destination Current => _dest.Selected;

    // ---------- layout ----------

    private void ApplyMode()
    {
        var d = Current;
        if (d == null) return;
        // No scrollbar: the text box scrolls with the caret and mouse wheel, and a native scrollbar clashes with the theme.
        _txt.ScrollBars = ScrollBars.None;
        switch (d.Mode)
        {
            case CaptureMode.Daily: _placeholder.Text = "New capture"; break;
            case CaptureMode.Journal: _placeholder.Text = "New entry"; break;
            case CaptureMode.Task: _placeholder.Text = "New task"; break;
            case CaptureMode.Inbox: _placeholder.Text = "Note title  (Shift+Enter for the body)"; break;
        }
        _placeholder.Visible = _txt.TextLength == 0;

        bool dated = d.Mode == CaptureMode.Daily || d.Mode == CaptureMode.Journal;
        _date.Visible = dated;
        _start.Visible = _due.Visible = d.Mode == CaptureMode.Task;
        Layout2();
        Invalidate();
    }

    private void Layout2()
    {
        int W = S(620), M = S(20);
        bool inbox = Current?.Mode == CaptureMode.Inbox;

        int textTop = S(18);
        int textH = inbox ? S(150) : S(66);
        _txt.SetBounds(M, textTop, W - 2 * M, textH);
        // TextBox has a few px of internal left margin; line the placeholder up with the caret.
        _placeholder.Location = new Point(M + S(1), textTop);

        // icon row (bottom-right of the text area)
        int rowY = textTop + textH + S(4), rowH = S(28);
        int x = W - M + S(6);
        var mode = Current?.Mode ?? CaptureMode.Daily;
        var icons = mode == CaptureMode.Task ? new Control[] { _due, _start }
                  : mode == CaptureMode.Inbox ? new Control[0] : new Control[] { _date };
        foreach (var c in icons)
        {
            int w = ((DatePill)c).PreferredCompactWidth;
            x -= w;
            c.SetBounds(x, rowY, w, rowH);
            x -= S(2);
        }
        x -= S(28);
        _btnAttach.SetBounds(x, rowY, S(28), rowH);
        _lblFiles.SetBounds(M, rowY, Math.Max(0, x - M - S(8)), rowH);

        _dividerY = rowY + rowH + S(10);

        // footer
        int fy = _dividerY + S(11), fh = S(34);
        _dest.SetBounds(M - S(8), fy, Math.Min(S(320), _dest.PreferredWidthNow), fh);
        _btnSave.SetBounds(W - M - S(70), fy, S(70), fh);
        _btnCancel.SetBounds(W - M - S(70) - S(10) - S(84), fy, S(84), fh);

        ClientSize = new Size(W, fy + fh + S(12));
        Invalidate();
    }

    // ---------- show / hide ----------

    /// <summary>Self-test hook: sets up the window for a destination without showing or activating it.</summary>
    internal void PrepareForTest(int destIndex, string text, string file = null)
    {
        _s = _getSettings();
        RebuildDestinations();
        _dest.SelectedIndex = Math.Min(destIndex, _dests.Count - 1);
        _date.Value = DateTime.Today;
        _start.Value = text.Length > 0 && Current?.Mode == CaptureMode.Task ? DateTime.Today.AddDays(1) : (DateTime?)null;
        _due.Value = text.Length > 0 && Current?.Mode == CaptureMode.Task ? DateTime.Today.AddDays(5) : (DateTime?)null;
        _files.Clear();
        if (file != null) _files.Add(file);
        _txt.Text = text;
        ApplyMode();
        UpdateFiles();
    }

    internal int DestinationCount => _dests.Count;

    public void ShowCapture()
    {
        _s = _getSettings();
        bool hasDraft = _txt.TextLength > 0 || _files.Count > 0;

        RebuildDestinations();
        if (!hasDraft)
        {
            int idx = 0;
            if (_s.RememberLastDestination && _s.LastDestination != null)
                idx = Math.Max(0, _dests.FindIndex(d => d.Key == _s.LastDestination));
            _dest.SelectedIndex = idx;
            _date.Value = DateTime.Today;
            _start.Value = null;
            _due.Value = null;
        }

        _btnSave.Text = "Save";
        ApplyMode();
        UpdateFiles();

        if (!Visible)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (int)(wa.Height * 0.24));
        }
        Show();
        Activate();
        BringToFront();
        Native.SetForegroundWindow(Handle);
        _txt.Focus();
        _txt.SelectionStart = _txt.TextLength;
    }

    public void ToggleCapture()
    {
        if (Visible && ActiveForm == this) Hide();
        else ShowCapture();
    }

    // ---------- keyboard ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (PickerOpen) return base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.Escape:
                Hide();
                return true;
            case Keys.Enter:
            case Keys.Control | Keys.Enter:
                DoSave();
                return true;
            case Keys.Shift | Keys.Enter:
                if (Current?.Mode == CaptureMode.Inbox && _txt.Focused) { _txt.SelectedText = "\r\n"; return true; }
                DoSave();
                return true;
            case Keys.Control | Keys.A:
                if (_txt.Focused) { _txt.SelectAll(); return true; }
                break;
            case Keys.Control | Keys.O:
                PickFiles();
                return true;
            case Keys.Control | Keys.Oemcomma:
                SettingsRequested?.Invoke();
                return true;
        }

        if ((keyData & Keys.Modifiers) == Keys.Control)
        {
            string letter = (keyData & Keys.KeyCode).ToString();
            if (letter.Length == 1)
            {
                int idx = _dests.FindIndex(d => d.Shortcut.Equals(letter, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) { _dest.SelectedIndex = idx; _txt.Focus(); return true; }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public event Action SettingsRequested;

    // ---------- attachments ----------

    private void PickFiles()
    {
        _modalOpen = true;
        try
        {
            using (var dlg = new OpenFileDialog { Multiselect = true, Title = "Attach files to this capture" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) { _files.AddRange(dlg.FileNames); UpdateFiles(); }
            }
        }
        finally { _modalOpen = false; }
        Activate();
        _txt.Focus();
    }

    private void UpdateFiles()
    {
        _lblFiles.Text = _files.Count == 0 ? ""
            : "📎 " + string.Join(", ", _files.Select(Path.GetFileName)) + "  ✕";
        _btnAttach.Active = _files.Count > 0;
        _btnAttach.Invalidate();
    }

    // ---------- save ----------

    private void DoSave()
    {
        var d = Current;
        if (d == null) return;
        if (string.IsNullOrEmpty(_s.VaultPath) || !Directory.Exists(_s.VaultPath))
        {
            ShowError("Choose your Obsidian vault in Settings first (right-click the tray icon → Settings).");
            return;
        }
        if (_txt.Text.Trim().Length == 0 && _files.Count == 0) { System.Media.SystemSounds.Beep.Play(); return; }

        var req = new CaptureRequest
        {
            Mode = d.Mode,
            Text = _txt.Text,
            NoteDate = (_date.Value ?? DateTime.Today).Date,
            Journal = d.Journal,
            StartDate = _start.Value,
            DueDate = _due.Value,
            Attachments = new List<string>(_files),
        };

        _saving = true;
        try
        {
            new CaptureWriter(_s, _s.VaultPath).Save(req);
            if (_s.RememberLastDestination) { _s.LastDestination = d.Key; _saveSettings(); }
            _txt.Clear();
            _files.Clear();
            UpdateFiles();
            _btnSave.Text = "Saved ✓";
            Refresh();
            var t = new Timer { Interval = 400 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); _btnSave.Text = "Save"; Hide(); };
            t.Start();
        }
        catch (Exception ex)
        {
            ShowError("Couldn't save: " + ex.Message);
        }
        finally { _saving = false; }
    }

    private void ShowError(string message)
    {
        _modalOpen = true;
        try { MessageBox.Show(this, message, "VaultCapture", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _modalOpen = false; }
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }
}
