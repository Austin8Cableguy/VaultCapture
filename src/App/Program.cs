using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using VaultCapture.Core;

namespace VaultCapture.App;

internal static class Program
{
    private const string ShowEventName = "VaultCapture.ShowCaptureWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SelfTest.Run();
            return;
        }

        using (var mutex = new Mutex(true, "VaultCapture.SingleInstance", out bool first))
        {
            if (!first)
            {
                // Already running: ask that instance to show its capture window.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                return;
            }

            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                Application.Run(new TrayContext(showEvent));
            }
        }
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "VaultCapture";

    private AppSettings _settings;
    private readonly NotifyIcon _tray = new NotifyIcon();
    private readonly HotkeyWindow _hotkey = new HotkeyWindow();
    private readonly CaptureForm _form;
    private readonly ToolStripMenuItem _miCapture;
    private readonly ToolStripMenuItem _miStartup;
    private bool _settingsOpen;

    public TrayContext(EventWaitHandle showEvent)
    {
        _settings = AppSettings.Load();
        bool firstRun = !File.Exists(AppSettings.SettingsFile);
        EnsureVault();
        ImportFromVault();
        _settings.Save();
        Theme.Apply(_settings.Theme);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _form = new CaptureForm(() => _settings, () => _settings.Save());
        _form.SettingsRequested += () => { _form.Hide(); ShowSettings(); };
        var _ = _form.Handle; // create handle so BeginInvoke works before first show

        _hotkey.Pressed += () => _form.ToggleCapture();

        var menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer() };
        _miCapture = new ToolStripMenuItem("Quick capture", null, (o, e) => _form.ShowCapture());
        _miCapture.Font = new Font(_miCapture.Font, FontStyle.Bold);
        _miStartup = new ToolStripMenuItem("Start with Windows", null, (o, e) => { SetStartup(!IsStartupEnabled()); _miStartup.Checked = IsStartupEnabled(); });
        menu.Items.Add(_miCapture);
        menu.Items.Add("Settings…", null, (o, e) => ShowSettings());
        menu.Items.Add("Open vault folder", null, (o, e) => OpenVault());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (o, e) => ExitThread());
        menu.Opening += (o, e) => _miStartup.Checked = IsStartupEnabled();

        _tray.Icon = LoadIcon();
        _tray.Text = "VaultCapture";
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) _form.ShowCapture(); };
        _tray.Visible = true;

        RegisterHotkey(showBalloonOnSuccess: firstRun);

        ThreadPool.RegisterWaitForSingleObject(showEvent,
            (state, timedOut) => { try { _form.BeginInvoke((Action)_form.ShowCapture); } catch { } },
            null, Timeout.Infinite, false);

        if (string.IsNullOrEmpty(_settings.VaultPath))
            _form.BeginInvoke((Action)(() =>
            {
                MessageBox.Show("VaultCapture couldn't find an Obsidian vault automatically. Please choose your vault folder.",
                    "VaultCapture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowSettings();
            }));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Fires when Windows switches between light and dark mode.
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
            _form.BeginInvoke((Action)(() => Theme.Apply(_settings.Theme)));
    }

    internal static Icon LoadIcon()
    {
        try
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VaultCapture.app.ico"))
                if (s != null) return new Icon(s, SystemInformation.SmallIconSize);
        }
        catch { }
        return SystemIcons.Application;
    }

    private void EnsureVault()
    {
        if (!string.IsNullOrEmpty(_settings.VaultPath) && Directory.Exists(_settings.VaultPath)) return;
        var v = ObsidianVault.PickDefault(ObsidianVault.FindVaults());
        _settings.VaultPath = v?.Path;
    }

    private void ImportFromVault()
    {
        if (string.IsNullOrEmpty(_settings.VaultPath) || !_settings.ImportDailySettingsFromVault) return;
        var cfg = ObsidianVault.ReadDailyConfig(_settings.VaultPath);
        if (cfg == null) return;
        _settings.DailyFolder = cfg.Folder;
        _settings.DailyFormat = cfg.Format;
        _settings.DailyTemplate = cfg.Template;
    }

    private void RegisterHotkey(bool showBalloonOnSuccess)
    {
        bool ok = _hotkey.Register(_settings.Hotkey);
        _miCapture.ShortcutKeyDisplayString = _settings.Hotkey;
        _tray.Text = "VaultCapture (" + _settings.Hotkey + ")";
        if (!ok)
            _tray.ShowBalloonTip(6000, "VaultCapture", "The hotkey " + _settings.Hotkey +
                " is already used by another app. Click the tray icon to capture, or pick a new hotkey in Settings.", ToolTipIcon.Warning);
        else if (showBalloonOnSuccess)
            _tray.ShowBalloonTip(5000, "VaultCapture is running", "Press " + _settings.Hotkey +
                " anywhere to capture to your Obsidian vault. Right-click the tray icon for settings.", ToolTipIcon.Info);
    }

    private void ShowSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        _hotkey.Unregister(); // so pressing the current hotkey in the hotkey box doesn't trigger it
        try
        {
            using (var f = new SettingsForm(_settings.Clone(), IsStartupEnabled()))
            {
                if (f.ShowDialog() == DialogResult.OK && f.Result != null)
                {
                    bool vaultChanged = !string.Equals(f.Result.VaultPath, _settings.VaultPath, StringComparison.OrdinalIgnoreCase);
                    _settings = f.Result;
                    Theme.Apply(_settings.Theme);
                    if (vaultChanged) ImportFromVault();
                    _settings.Save();
                    if (f.StartWithWindows != IsStartupEnabled()) SetStartup(f.StartWithWindows);
                }
            }
        }
        finally
        {
            _settingsOpen = false;
            RegisterHotkey(false);
        }
    }

    private void OpenVault()
    {
        if (!string.IsNullOrEmpty(_settings.VaultPath) && Directory.Exists(_settings.VaultPath))
            Process.Start("explorer.exe", "\"" + _settings.VaultPath + "\"");
    }

    private static bool IsStartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k?.GetValue(RunName) is string s && s.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void SetStartup(bool enable)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enable) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(RunName, false);
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tray.Visible = false;
        _tray.Dispose();
        _hotkey.Dispose();
        _form.Dispose();
        base.ExitThreadCore();
    }
}
