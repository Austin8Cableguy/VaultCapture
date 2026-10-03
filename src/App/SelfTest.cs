using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using VaultCapture.Core;

namespace VaultCapture.App;

/// <summary>
/// "VaultCapture.exe --selftest": checks the environment, exercises note writing in a throwaway
/// vault, and saves screenshots of every window in both themes. Never writes to the real vault.
/// </summary>
internal static class SelfTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _pass, _fail, _warn;
    private static string _out;

    public static void Run()
    {
        _out = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-results");
        try { if (Directory.Exists(_out)) Directory.Delete(_out, true); } catch { }
        Directory.CreateDirectory(_out);

        Line("VaultCapture self-test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Line(new string('=', 60));

        Section("Environment", Environment_);
        Section("Obsidian vault detection (read-only)", Vaults);
        Section("Note writing (temporary test vault)", Writing);
        Section("Global hotkey", Hotkeys);
        Section("Tray icon", () => Check("Embedded icon loads", TrayContext.LoadIcon() != SystemIcons.Application));
        Section("Capture window screenshots", CaptureShots);
        Section("Settings window screenshots", SettingsShots);

        Line("");
        Line($"RESULT: {_pass} passed, {_fail} failed, {_warn} warnings");
        File.WriteAllText(Path.Combine(_out, "report.txt"), Log.ToString(), new UTF8Encoding(false));

        MessageBox.Show($"Self-test finished.\n\n{_pass} passed, {_fail} failed, {_warn} warnings.\n\nResults were saved to:\n{_out}\n\nYou can tell Claude it's done.",
            "VaultCapture self-test", MessageBoxButtons.OK, _fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        try { Process.Start("explorer.exe", "\"" + _out + "\""); } catch { }
    }

    // ---------- logging ----------

    private static void Line(string s) => Log.AppendLine(s);

    private static void Section(string name, Action body)
    {
        Line("");
        Line("## " + name);
        try { body(); }
        catch (Exception ex) { _fail++; Line("  FAIL  section crashed: " + ex); }
    }

    private static void Check(string name, bool ok, string detail = null)
    {
        if (ok) _pass++; else _fail++;
        Line((ok ? "  PASS  " : "  FAIL  ") + name + (detail != null ? "  —  " + detail : ""));
    }

    private static void Warn(string name, string detail) { _warn++; Line("  WARN  " + name + "  —  " + detail); }
    private static void Info(string s) => Line("  info  " + s);

    private static void Try(string name, Action a)
    {
        try { a(); Check(name, true); }
        catch (Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); }
    }

    // ---------- sections ----------

    private static void Environment_()
    {
        Info("Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : ""));
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                Info("Build: " + k?.GetValue("ProductName") + " " + k?.GetValue("DisplayVersion") + " (build " + k?.GetValue("CurrentBuild") + ")");
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                int rel = k?.GetValue("Release") is int r ? r : 0;
                Check(".NET Framework 4.8+ installed", rel >= 528040, "release " + rel);
            }
        }
        catch (Exception ex) { Warn("Registry read", ex.Message); }

        using (var g = Graphics.FromHwnd(IntPtr.Zero))
            Info($"Display scaling: {g.DpiX / 96f:P0} ({g.DpiX} DPI); primary screen {Screen.PrimaryScreen.Bounds.Width}x{Screen.PrimaryScreen.Bounds.Height}");
        Info("Windows app theme: " + (Theme.IsSystemDark() ? "Dark" : "Light"));

        var fonts = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
        foreach (var f in new[] { "Segoe UI", "Segoe UI Semibold", "Segoe MDL2 Assets", "Segoe UI Emoji" })
            Check("Font available: " + f, fonts.Contains(f));

        Info("Settings file: " + AppSettings.SettingsFile + (File.Exists(AppSettings.SettingsFile) ? " (exists)" : " (not created yet)"));
        bool running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath)).Length > 1;
        Info("Another VaultCapture is running: " + (running ? "yes" : "no"));
    }

    private static void Vaults()
    {
        var vaults = ObsidianVault.FindVaults();
        Check("Obsidian vault list found", vaults.Count > 0, vaults.Count + " vault(s)");
        foreach (var v in vaults) Info($"vault: {v.Name}{(v.Open ? "  [open]" : "")}  {v.Path}");

        var s = AppSettings.Load();
        var vault = !string.IsNullOrEmpty(s.VaultPath) ? s.VaultPath : ObsidianVault.PickDefault(vaults)?.Path;
        if (vault == null) { Warn("No vault to inspect", "skipping vault settings checks"); return; }
        Info("Inspecting (read-only): " + vault);
        Check("Vault folder exists", Directory.Exists(vault));
        Check("Vault is writable by this user", IsWritable(vault));

        var dc = ObsidianVault.ReadDailyConfig(vault);
        if (dc == null) Warn("Daily notes config", "not found; VaultCapture will use YYYY-MM-DD in the vault root");
        else
        {
            Info($"daily notes: folder='{dc.Folder}' format='{dc.Format}' template='{dc.Template}'");
            var w = new CaptureWriter(new AppSettings { DailyFolder = dc.Folder, DailyFormat = dc.Format }, vault);
            var todayPath = w.DailyNotePath(DateTime.Today);
            Info("today's daily note would be: " + CaptureWriter.RelativePath(vault, todayPath) + (File.Exists(todayPath) ? " (exists)" : " (would be created)"));
            if (!string.IsNullOrWhiteSpace(dc.Template))
            {
                var tf = ObsidianVault.ReadTemplatesFolder(vault);
                bool found = new[] { dc.Template, dc.Template + ".md", tf == null ? null : tf + "/" + dc.Template + ".md" }
                    .Where(x => x != null).Any(x => File.Exists(w.VaultPath(x)));
                Check("Daily template file found", found, dc.Template);
            }
        }
        Info("attachment folder setting: '" + ObsidianVault.ReadAttachmentFolder(vault) + "'");
        Info("Tasks plugin global filter: " + (ObsidianVault.ReadTasksGlobalFilter(vault) ?? "(none)"));
        if (vault.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
            Info("vault is in OneDrive: writes go to the local copy and OneDrive syncs them as usual");
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var p = Path.Combine(dir, ".vaultcapture-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(p, "x");
            File.Delete(p);
            return true;
        }
        catch { return false; }
    }

    private static void Writing()
    {
        var vault = Path.Combine(Path.GetTempPath(), "VaultCapture-selftest-vault");
        if (Directory.Exists(vault)) Directory.Delete(vault, true);
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(vault, "Templates"));
        File.WriteAllText(Path.Combine(vault, ".obsidian", "daily-notes.json"), "{\"folder\":\"Daily\",\"format\":\"YYYY/MM/YYYY-MM-DD\",\"template\":\"Templates/Daily\"}");
        File.WriteAllText(Path.Combine(vault, ".obsidian", "app.json"), "{\"attachmentFolderPath\":\"Attachments\"}");
        File.WriteAllText(Path.Combine(vault, "Templates", "Daily.md"), "# {{title}}\r\n\r\n## Journal\r\n\r\n## Gratitude\r\n");
        var attach = Path.Combine(_out, "sample attachment.txt");
        File.WriteAllText(attach, "attachment");

        var dc = ObsidianVault.ReadDailyConfig(vault);
        var s = new AppSettings { DailyFolder = dc.Folder, DailyFormat = dc.Format, DailyTemplate = dc.Template, TaskTag = "#task", AutoDetectTaskTag = false };
        var w = new CaptureWriter(s, vault);
        var day = DateTime.Today;

        string daily = null, tasks = null, inbox = null;
        Try("Daily capture creates note from template", () => daily = w.Save(new CaptureRequest { Mode = CaptureMode.Daily, Text = "Self-test idé ✓ — unicode", NoteDate = day }));
        Try("Journal: gratitude", () => w.Save(new CaptureRequest { Mode = CaptureMode.Journal, Text = "Coffee", NoteDate = day, Journal = s.JournalTypes[1] }));
        Try("Journal: win (new heading)", () => w.Save(new CaptureRequest { Mode = CaptureMode.Journal, Text = "Shipped it", NoteDate = day, Journal = s.JournalTypes[2] }));
        Try("Daily capture with attachment", () => w.Save(new CaptureRequest { Mode = CaptureMode.Daily, Text = "With file", NoteDate = day, Attachments = { attach } }));
        Try("Task with dates", () => tasks = w.Save(new CaptureRequest { Mode = CaptureMode.Task, Text = "Call the bank", StartDate = day, DueDate = day.AddDays(2) }));
        Try("Inbox note", () => inbox = w.Save(new CaptureRequest { Mode = CaptureMode.Inbox, Text = "Big idea: test\r\nBody line" }));

        if (daily != null)
        {
            var t = File.ReadAllText(daily);
            Check("Daily note in dated subfolder", daily.Replace('\\', '/').Contains("/Daily/" + day.ToString("yyyy") + "/" + day.ToString("MM") + "/"));
            Check("Template rendered", t.StartsWith("# " + day.ToString("yyyy-MM-dd")));
            Check("Unicode preserved", t.Contains("Self-test idé ✓ — unicode"));
            Check("Gratitude under its heading", t.Contains("## Gratitude\r\n- Coffee #journal/gratitude"));
            Check("CRLF template line endings kept", t.Contains("\r\n"));
            Check("Captured section added", t.Contains("## Captured"));
            Check("Attachment copied and embedded", t.Contains("![[Attachments/sample attachment.txt]]") && File.Exists(Path.Combine(vault, "Attachments", "sample attachment.txt")));
            File.Copy(daily, Path.Combine(_out, "sample-daily-note.md"), true);
        }
        if (tasks != null)
        {
            var t = File.ReadAllText(tasks);
            Check("Task line format", t.Contains("- [ ] #task Call the bank 🛫 " + day.ToString("yyyy-MM-dd") + " 📅 " + day.AddDays(2).ToString("yyyy-MM-dd")), t.Trim());
        }
        if (inbox != null)
        {
            Check("Inbox title sanitized", Path.GetFileName(inbox) == "Big idea test.md", Path.GetFileName(inbox));
            Check("Inbox body", File.ReadAllText(inbox) == "Body line\n");
        }
        try { Directory.Delete(vault, true); } catch { }
    }

    private static void Hotkeys()
    {
        using (var hk = new HotkeyWindow())
        {
            Check("Can register a test hotkey (Ctrl+Alt+Shift+F12)", hk.Register("Ctrl+Alt+Shift+F12"));
            hk.Unregister();

            var configured = AppSettings.Load().Hotkey;
            bool ok = hk.Register(configured);
            hk.Unregister();
            bool running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath)).Length > 1;
            if (ok) Check("Configured hotkey is free: " + configured, true);
            else if (running) Info("Configured hotkey " + configured + " is held by the running VaultCapture (expected)");
            else Check("Configured hotkey is free: " + configured, false, "another app is using it — pick a different one in Settings");
        }
    }

    private static void CaptureShots()
    {
        var settings = AppSettings.Load();
        if (string.IsNullOrEmpty(settings.VaultPath)) settings.VaultPath = Path.GetTempPath();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            Theme.Apply(theme);
            using (var f = new CaptureForm(() => settings, () => { }))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.Show();
                var samples = new[] { "Idea: weekly review template", "Coffee with Sam this morning", "", "", "Call the bank about the card", "Big idea\r\nSecond line of the body" };
                for (int i = 0; i < f.DestinationCount; i++)
                {
                    string text = i < samples.Length ? samples[i] : "Sample";
                    f.PrepareForTest(i, text, i == 0 ? @"C:\Users\Example\Pictures\whiteboard.png" : null);
                    Application.DoEvents();
                    Save(f, $"capture-{theme.ToLower()}-{i}.png");
                }
                f.PrepareForTest(1, "");
                Application.DoEvents();
                Save(f, $"capture-{theme.ToLower()}-empty.png");
                f.Hide();
            }

            // calendar popup content
            using (var host = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), Font = new Font("Segoe UI", 9.75f) })
            {
                var cal = new CalendarGrid(DateTime.Today, DateTime.Today.AddDays(2), true) { Font = host.Font };
                host.ClientSize = cal.Size;
                host.Controls.Add(cal);
                host.Show();
                Application.DoEvents();
                Save(host, $"calendar-{theme.ToLower()}.png");
            }
        }
        Theme.Apply("System");
    }

    private static void SettingsShots()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var s = AppSettings.Load();
            s.Theme = theme;
            Theme.Apply(theme);
            using (var f = new SettingsForm(s, false))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.TopMost = false;
                f.Show();
                Application.DoEvents();
                for (int i = 0; i < f.PageCount; i++)
                {
                    string name = f.ShowPageForTest(i);
                    Application.DoEvents();
                    Save(f, $"settings-{theme.ToLower()}-{i}-{name.Split(' ')[0].ToLower()}.png");
                }
                f.DialogResult = DialogResult.OK; // don't trigger the theme revert path
                f.Hide();
            }
        }
        Theme.Apply("System");
    }

    private static void Save(Control c, string file)
    {
        try
        {
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(Path.Combine(_out, file), ImageFormat.Png);
            }
            Check("Screenshot " + file, true);
        }
        catch (Exception ex) { Check("Screenshot " + file, false, ex.Message); }
    }
}
