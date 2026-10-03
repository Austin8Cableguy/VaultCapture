using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VaultCapture.Core;

namespace VaultCapture.App;

internal sealed class SettingsForm : Form
{
    public AppSettings Result { get; private set; }
    public bool StartWithWindows => _chkStartup.Checked;

    private readonly AppSettings _s;
    private readonly ComboBox _cboVault = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly TextBox _txtHotkey = new TextBox { ReadOnly = true, Width = 220 };
    private readonly CheckBox _chkRemember = new CheckBox { Text = "Remember last used destination", AutoSize = true };
    private readonly ComboBox _cboTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly string _originalTheme;
    private readonly Panel _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
    private readonly Panel _nav = new Panel { Dock = DockStyle.Left, Width = 160, Padding = new Padding(8, 12, 8, 8), Tag = "keepcolor" };
    private readonly System.Collections.Generic.List<NavItem> _navItems = new System.Collections.Generic.List<NavItem>();
    private readonly CheckBox _chkStartup = new CheckBox { Text = "Start VaultCapture when I sign in to Windows", AutoSize = true };

    private readonly CheckBox _chkImportDaily = new CheckBox { Text = "Import these from the vault's Daily notes settings at startup", AutoSize = true };
    private readonly TextBox _txtDailyFolder = new TextBox { Dock = DockStyle.Fill };
    private readonly TextBox _txtDailyFormat = new TextBox { Dock = DockStyle.Fill };
    private readonly TextBox _txtDailyTemplate = new TextBox { Dock = DockStyle.Fill };
    private readonly CheckBox _chkUseTemplate = new CheckBox { Text = "Use the template when VaultCapture creates a new daily note", AutoSize = true };
    private readonly TextBox _txtCaptured = new TextBox { Dock = DockStyle.Fill };

    private readonly BindingList<JournalType> _journal;
    private readonly DataGridView _grid = new DataGridView();

    private readonly TextBox _txtTaskNote = new TextBox { Dock = DockStyle.Fill };
    private readonly CheckBox _chkAutoTag = new CheckBox { Text = "Use the Tasks plugin's global filter tag if set", AutoSize = true };
    private readonly TextBox _txtTaskTag = new TextBox { Width = 160 };
    private readonly RadioButton _rbTop = new RadioButton { Text = "Top of the note", AutoSize = true };
    private readonly RadioButton _rbBottom = new RadioButton { Text = "Bottom of the note", AutoSize = true };

    private readonly TextBox _txtInbox = new TextBox { Dock = DockStyle.Fill };
    private readonly RadioButton _rbEmbed = new RadioButton { Text = "Embed  ( ![[file]] )", AutoSize = true };
    private readonly RadioButton _rbLink = new RadioButton { Text = "Link  ( [[file]] )", AutoSize = true };
    private readonly TextBox _txtAttach = new TextBox { Dock = DockStyle.Fill };

    public SettingsForm(AppSettings settings, bool startWithWindows)
    {
        _s = settings;
        Text = "VaultCapture Settings";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 540);
        ShowInTaskbar = true;
        TopMost = true;

        _originalTheme = _s.Theme ?? "System";
        _journal = new BindingList<JournalType>(_s.JournalTypes.Select(j => new JournalType { Label = j.Label, Heading = j.Heading, Tag = j.Tag, Shortcut = j.Shortcut }).ToList())
        { AllowNew = true, AllowRemove = true };
        foreach (var page in new[] { GeneralTab(), DailyTab(), JournalTab(), TasksTab(), InboxTab() })
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
            var item = new NavItem { Text = page.Text, Dock = DockStyle.Top, Height = 36, Page = page };
            item.Click += (o, e) => SelectPage(item);
            _navItems.Add(item);
        }
        for (int i = _navItems.Count - 1; i >= 0; i--) _nav.Controls.Add(_navItems[i]);

        var ok = new Button { Text = "Save", Width = 90, Height = 32, Tag = "accent" };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        ok.Click += (o, e) => OnSave();
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(8, 10, 8, 8) };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        CancelButton = cancel;

        Controls.Add(_content);
        Controls.Add(_nav);
        Controls.Add(bottom);

        LoadValues(startWithWindows);
        SelectPage(_navItems[0]);
        ApplyTheme();
        Theme.Changed += ApplyTheme;
        FormClosed += (o, e) =>
        {
            Theme.Changed -= ApplyTheme;
            if (DialogResult != DialogResult.OK) Theme.Apply(_originalTheme); // undo live preview
        };
    }

    internal int PageCount => _navItems.Count;
    internal string ShowPageForTest(int i) { SelectPage(_navItems[i]); return _navItems[i].Text; }

    private void SelectPage(NavItem item)
    {
        foreach (var n in _navItems) { n.Selected = n == item; n.Page.Visible = n == item; n.Invalidate(); }
    }

    private void ApplyTheme()
    {
        var p = Theme.Current;
        BackColor = p.Window;
        ForeColor = p.Text;
        _nav.BackColor = p.Surface;
        Theme.StyleTree(this);
        Theme.TitleBar(this);
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke((Action)ApplyTheme);
    }

    // ---------- tab builders ----------

    private static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void Row(TableLayoutPanel t, string label, Control c, string help = null)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) };
        c.Margin = new Padding(0, 3, 0, 3);
        t.Controls.Add(l);
        t.Controls.Add(c);
        if (help != null) Help(t, help);
    }

    private static void Full(TableLayoutPanel t, Control c)
    {
        c.Margin = new Padding(0, 6, 0, 2);
        t.Controls.Add(c);
        t.SetColumnSpan(c, 2);
    }

    private static void Help(TableLayoutPanel t, string text)
    {
        t.Controls.Add(new Label());
        t.Controls.Add(new Label { Text = text, AutoSize = true, Tag = "muted", MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 8) });
    }

    private Control WithBrowse(Control input, Action onBrowse)
    {
        var p = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var b = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        b.Click += (o, e) => onBrowse();
        input.Dock = DockStyle.Fill;
        p.Controls.Add(input);
        p.Controls.Add(b);
        return p;
    }

    private Panel GeneralTab()
    {
        var page = new Panel { Text = "General" };
        var t = Grid();
        foreach (var v in ObsidianVault.FindVaults()) _cboVault.Items.Add(v.Path);
        Row(t, "Obsidian vault", WithBrowse(_cboVault, BrowseVault),
            "Vaults you've opened in Obsidian are listed automatically. Obsidian doesn't need to be running.");

        _txtHotkey.KeyDown += (o, e) =>
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) { _txtHotkey.Text = ""; return; }
            _txtHotkey.Text = Hotkey.Format(e.KeyData);
        };
        _txtHotkey.KeyUp += (o, e) => { if (_txtHotkey.Text.EndsWith("+")) _txtHotkey.Text = _s.Hotkey; };
        _txtHotkey.Enter += (o, e) => _txtHotkey.BackColor = Theme.Current.Selection;
        _txtHotkey.Leave += (o, e) => _txtHotkey.BackColor = Theme.Current.Surface;
        Row(t, "Global hotkey", _txtHotkey, "Click the box and press the key combination you want (e.g. Ctrl+Shift+Space).");
        _cboTheme.Items.AddRange(new object[] { "System", "Light", "Dark" });
        _cboTheme.SelectedIndexChanged += (o, e) => Theme.Apply((string)_cboTheme.SelectedItem);
        Row(t, "Theme", _cboTheme, "“System” follows your Windows light/dark setting.");
        Full(t, _chkRemember);
        Full(t, _chkStartup);
        page.Controls.Add(t);
        return page;
    }

    private Panel DailyTab()
    {
        var page = new Panel { Text = "Daily notes" };
        var t = Grid();
        Full(t, _chkImportDaily);
        Row(t, "Daily notes folder", WithBrowse(_txtDailyFolder, () => BrowseFolder(_txtDailyFolder)));
        Row(t, "Date format", _txtDailyFormat, "Moment.js format, same as Obsidian. Slashes create subfolders, e.g. YYYY/MM/YYYY-MM-DD.");
        Row(t, "Template file", WithBrowse(_txtDailyTemplate, () => BrowseFile(_txtDailyTemplate, true)));
        Full(t, _chkUseTemplate);
        Help(t, "Supports {{title}}, {{date}}, {{time}} and {{date:FORMAT}}. Templater code isn't run.");
        Row(t, "“Captured” heading", _txtCaptured, "Default captures go under this heading. Created at the end of the note if missing.");
        var refresh = new Button { Text = "Import from vault now", AutoSize = true };
        refresh.Click += (o, e) => ImportDailyNow(true);
        Full(t, refresh);
        page.Controls.Add(t);
        return page;
    }

    private Panel JournalTab()
    {
        var page = new Panel { Text = "Journal" };
        var t = Grid();
        t.RowStyles.Clear();
        var intro = new Label
        {
            Text = "Each row is a destination in the capture menu. Entries are added as bullets under the heading in your daily note, " +
                   "with the tag appended. Shortcut is a letter used with Ctrl. Use the empty last row to add a type; select a row and press Delete to remove it.",
            AutoSize = true, MaximumSize = new Size(590, 0), Margin = new Padding(0, 0, 0, 8),
        };
        Full(t, intro);
        _grid.AutoGenerateColumns = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Label", HeaderText = "Menu label", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Heading", HeaderText = "Daily note heading", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Tag", HeaderText = "Tag", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Shortcut", HeaderText = "Ctrl+", Width = 60, MaxInputLength = 1 });
        _grid.DataSource = _journal;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.RowHeadersWidth = 28;
        _grid.Height = 300;
        _grid.Dock = DockStyle.Fill;
        Full(t, _grid);
        page.Controls.Add(t);
        return page;
    }

    private Panel TasksTab()
    {
        var page = new Panel { Text = "Tasks" };
        var t = Grid();
        Row(t, "Master task note", WithBrowse(_txtTaskNote, () => BrowseFile(_txtTaskNote, false)),
            "Vault-relative path. Created if it doesn't exist.");
        Full(t, _chkAutoTag);
        Row(t, "Tag otherwise", _txtTaskTag, "e.g. #task. Leave blank for none.");
        var where = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        where.Controls.Add(_rbTop);
        where.Controls.Add(_rbBottom);
        Row(t, "Add new tasks to", where);
        Help(t, "Start (🛫) and due (📅) dates use the Tasks plugin emoji format.");
        page.Controls.Add(t);
        return page;
    }

    private Panel InboxTab()
    {
        var page = new Panel { Text = "Inbox & files" };
        var t = Grid();
        Row(t, "Inbox folder", WithBrowse(_txtInbox, () => BrowseFolder(_txtInbox)),
            "Inbox captures become new notes here. First line = title, the rest = body.");
        var how = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        how.Controls.Add(_rbEmbed);
        how.Controls.Add(_rbLink);
        Row(t, "Attachments are", how);
        Row(t, "Attachment folder", WithBrowse(_txtAttach, () => BrowseFolder(_txtAttach)),
            "Leave blank to use the vault's own “Default location for new attachments” setting.");
        page.Controls.Add(t);
        return page;
    }

    // ---------- values ----------

    private void LoadValues(bool startWithWindows)
    {
        _cboVault.Text = _s.VaultPath ?? "";
        _txtHotkey.Text = _s.Hotkey;
        _chkRemember.Checked = _s.RememberLastDestination;
        _cboTheme.SelectedItem = new[] { "System", "Light", "Dark" }.Contains(_s.Theme) ? _s.Theme : "System";
        _chkStartup.Checked = startWithWindows;

        _chkImportDaily.Checked = _s.ImportDailySettingsFromVault;
        _txtDailyFolder.Text = _s.DailyFolder;
        _txtDailyFormat.Text = _s.DailyFormat;
        _txtDailyTemplate.Text = _s.DailyTemplate;
        _chkUseTemplate.Checked = _s.UseTemplateForNewDailyNotes;
        _txtCaptured.Text = _s.CapturedHeading;

        _txtTaskNote.Text = _s.TaskNotePath;
        _chkAutoTag.Checked = _s.AutoDetectTaskTag;
        _txtTaskTag.Text = _s.TaskTag;
        _rbTop.Checked = _s.TasksAtTop;
        _rbBottom.Checked = !_s.TasksAtTop;

        _txtInbox.Text = _s.InboxFolder;
        _rbEmbed.Checked = _s.EmbedAttachments;
        _rbLink.Checked = !_s.EmbedAttachments;
        _txtAttach.Text = _s.AttachmentFolderOverride;
    }

    private void OnSave()
    {
        string vault = _cboVault.Text.Trim();
        if (vault.Length > 0 && !Directory.Exists(vault))
        {
            MessageBox.Show(this, "That vault folder doesn't exist.", "VaultCapture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!Hotkey.TryParse(_txtHotkey.Text, out _))
        {
            MessageBox.Show(this, "Please set a valid hotkey (with Ctrl, Alt or Shift).", "VaultCapture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _grid.EndEdit();

        var r = _s.Clone();
        r.VaultPath = vault.Length > 0 ? vault : null;
        r.Hotkey = _txtHotkey.Text;
        r.RememberLastDestination = _chkRemember.Checked;
        r.Theme = (string)_cboTheme.SelectedItem ?? "System";
        r.ImportDailySettingsFromVault = _chkImportDaily.Checked;
        r.DailyFolder = _txtDailyFolder.Text.Trim();
        r.DailyFormat = _txtDailyFormat.Text.Trim().Length > 0 ? _txtDailyFormat.Text.Trim() : "YYYY-MM-DD";
        r.DailyTemplate = _txtDailyTemplate.Text.Trim();
        r.UseTemplateForNewDailyNotes = _chkUseTemplate.Checked;
        r.CapturedHeading = _txtCaptured.Text.Trim().Length > 0 ? _txtCaptured.Text.Trim() : "Captured";
        r.JournalTypes = _journal
            .Where(j => !string.IsNullOrWhiteSpace(j.Label))
            .Select(j => new JournalType
            {
                Label = j.Label.Trim(),
                Heading = string.IsNullOrWhiteSpace(j.Heading) ? j.Label.Trim() : j.Heading.Trim(),
                Tag = (j.Tag ?? "").Trim().TrimStart('#'),
                Shortcut = string.IsNullOrWhiteSpace(j.Shortcut) ? "" : j.Shortcut.Trim().Substring(0, 1).ToUpperInvariant(),
            }).ToList();
        r.TaskNotePath = _txtTaskNote.Text.Trim().Length > 0 ? _txtTaskNote.Text.Trim() : "Tasks.md";
        r.AutoDetectTaskTag = _chkAutoTag.Checked;
        r.TaskTag = _txtTaskTag.Text.Trim();
        r.TasksAtTop = _rbTop.Checked;
        r.InboxFolder = _txtInbox.Text.Trim();
        r.EmbedAttachments = _rbEmbed.Checked;
        r.AttachmentFolderOverride = _txtAttach.Text.Trim();

        Result = r;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ImportDailyNow(bool showMessage)
    {
        var vault = _cboVault.Text.Trim();
        var cfg = Directory.Exists(vault) ? ObsidianVault.ReadDailyConfig(vault) : null;
        if (cfg == null)
        {
            if (showMessage) MessageBox.Show(this, "No Daily notes settings found in that vault; keeping the current values.", "VaultCapture");
            return;
        }
        _txtDailyFolder.Text = cfg.Folder;
        _txtDailyFormat.Text = cfg.Format;
        _txtDailyTemplate.Text = cfg.Template;
    }

    // ---------- browse helpers ----------

    private string VaultRoot => _cboVault.Text.Trim();

    private string ToVaultRelative(string full)
    {
        if (Directory.Exists(VaultRoot))
        {
            var rel = CaptureWriter.RelativePath(VaultRoot, full);
            if (!Path.IsPathRooted(rel)) return rel.Replace('\\', '/');
        }
        MessageBox.Show(this, "Please pick something inside your vault.", "VaultCapture");
        return null;
    }

    private void BrowseVault()
    {
        using (var dlg = new FolderBrowserDialog { Description = "Choose your Obsidian vault folder" })
        {
            if (Directory.Exists(VaultRoot)) dlg.SelectedPath = VaultRoot;
            if (dlg.ShowDialog(this) == DialogResult.OK) _cboVault.Text = dlg.SelectedPath;
        }
    }

    private void BrowseFolder(TextBox target)
    {
        using (var dlg = new FolderBrowserDialog { Description = "Choose a folder inside your vault" })
        {
            dlg.SelectedPath = Directory.Exists(VaultRoot) ? Path.Combine(VaultRoot, target.Text.Replace('/', '\\')) : "";
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var rel = ToVaultRelative(dlg.SelectedPath);
                if (rel != null) target.Text = rel;
            }
        }
    }

    private void BrowseFile(TextBox target, bool stripMd)
    {
        using (var dlg = new OpenFileDialog { Filter = "Markdown notes (*.md)|*.md", CheckFileExists = false, Title = "Choose a note in your vault" })
        {
            if (Directory.Exists(VaultRoot)) dlg.InitialDirectory = VaultRoot;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var rel = ToVaultRelative(dlg.FileName);
                if (rel == null) return;
                if (stripMd && rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(0, rel.Length - 3);
                target.Text = rel;
            }
        }
    }
}

/// <summary>Sidebar entry in the settings window.</summary>
internal sealed class NavItem : ThemedControl
{
    public bool Selected;
    public Control Page;

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Surface);
        var r = new Rectangle(0, 2, Width - 1, Height - 5);
        if (Selected || Hover)
            using (var path = Theme.RoundRect(r, 6))
            using (var b = new SolidBrush(Selected ? p.Selection : p.SurfaceHover)) g.FillPath(b, path);
        if (Selected)
            using (var b = new SolidBrush(p.Accent)) g.FillRectangle(b, 0, r.Y + r.Height / 4, 3, r.Height / 2);
        using (var f = new Font(Font, Selected ? FontStyle.Bold : FontStyle.Regular))
            TextRenderer.DrawText(g, Text, f, new Rectangle(12, 0, Width - 12, Height), p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
