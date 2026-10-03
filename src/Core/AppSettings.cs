using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace VaultCapture.Core;

public class JournalType
{
    public string Label { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Tag { get; set; } = "";
    /// <summary>Single letter used with Ctrl to switch to this destination (e.g. "J").</summary>
    public string Shortcut { get; set; } = "";
}

public class AppSettings
{
    // General
    public string VaultPath { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Shift+Space";
    /// <summary>"System" (follow Windows), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public bool RememberLastDestination { get; set; } = true;
    public string LastDestination { get; set; }

    // Daily notes
    public bool ImportDailySettingsFromVault { get; set; } = true;
    public string DailyFolder { get; set; } = "";
    public string DailyFormat { get; set; } = "YYYY-MM-DD";
    public string DailyTemplate { get; set; } = "";
    public bool UseTemplateForNewDailyNotes { get; set; } = true;
    public string CapturedHeading { get; set; } = "Captured";

    // Journal
    public List<JournalType> JournalTypes { get; set; } = DefaultJournalTypes();

    // Tasks
    public string TaskNotePath { get; set; } = "Tasks.md";
    public bool AutoDetectTaskTag { get; set; } = true;
    public string TaskTag { get; set; } = "";
    public bool TasksAtTop { get; set; } = true;

    // Inbox
    public string InboxFolder { get; set; } = "Inbox";

    // Attachments
    public bool EmbedAttachments { get; set; } = true;
    /// <summary>Blank = use the attachment folder configured in the vault.</summary>
    public string AttachmentFolderOverride { get; set; } = "";

    public static List<JournalType> DefaultJournalTypes() => new List<JournalType>
    {
        new JournalType { Label = "Journal entry", Heading = "Journal", Tag = "journal/entry", Shortcut = "J" },
        new JournalType { Label = "Gratitude", Heading = "Gratitude", Tag = "journal/gratitude", Shortcut = "G" },
        new JournalType { Label = "Win", Heading = "Wins", Tag = "journal/win", Shortcut = "W" },
    };

    // ---- persistence ----
    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VaultCapture");

    public static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load(string path = null)
    {
        path = path ?? SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                var s = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(path));
                if (s != null)
                {
                    if (s.JournalTypes == null) s.JournalTypes = new List<JournalType>();
                    return s;
                }
            }
        }
        catch { /* corrupt settings: fall back to defaults */ }
        return new AppSettings();
    }

    public void Save(string path = null)
    {
        path = path ?? SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, PrettyJson(new JavaScriptSerializer().Serialize(this)), new UTF8Encoding(false));
    }

    public AppSettings Clone()
    {
        var js = new JavaScriptSerializer();
        return js.Deserialize<AppSettings>(js.Serialize(this));
    }

    /// <summary>Indents compact JSON so the settings file is readable by hand.</summary>
    public static string PrettyJson(string json)
    {
        var sb = new StringBuilder();
        int indent = 0;
        bool inStr = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                else if (c == '"') inStr = false;
                continue;
            }
            switch (c)
            {
                case '"': inStr = true; sb.Append(c); break;
                case '{':
                case '[':
                    sb.Append(c).Append('\n').Append(' ', ++indent * 2); break;
                case '}':
                case ']':
                    sb.Append('\n').Append(' ', --indent * 2).Append(c); break;
                case ',':
                    sb.Append(c).Append('\n').Append(' ', indent * 2); break;
                case ':':
                    sb.Append(": "); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
