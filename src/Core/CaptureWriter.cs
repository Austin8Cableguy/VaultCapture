using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VaultCapture.Core;

public enum CaptureMode { Daily, Journal, Task, Inbox }

public class CaptureRequest
{
    public CaptureMode Mode { get; set; }
    public string Text { get; set; } = "";
    public DateTime NoteDate { get; set; } = DateTime.Today;
    public JournalType Journal { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public List<string> Attachments { get; set; } = new();
}

/// <summary>Writes captures straight into the markdown files of an Obsidian vault.</summary>
public class CaptureWriter
{
    private readonly AppSettings _s;
    private readonly string _vault;
    private readonly Func<DateTime> _now;
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public CaptureWriter(AppSettings settings, string vaultPath, Func<DateTime> now = null)
    {
        _s = settings;
        _vault = vaultPath;
        _now = now ?? (() => DateTime.Now);
    }

    /// <summary>Saves the capture and returns the full path of the note that was written.</summary>
    public string Save(CaptureRequest r)
    {
        if (!Directory.Exists(_vault))
            throw new InvalidOperationException($"Vault folder not found: {_vault}");

        string text = (r.Text ?? "").Trim();
        if (text.Length == 0 && r.Attachments.Count == 0)
            throw new InvalidOperationException("Nothing to capture.");

        return r.Mode switch
        {
            CaptureMode.Daily => SaveToDaily(r.NoteDate, _s.CapturedHeading, BulletLine(text, null, r.Attachments, DailyNotePath(r.NoteDate))),
            CaptureMode.Journal => SaveToDaily(r.NoteDate, r.Journal?.Heading ?? "Journal",
                BulletLine(text, r.Journal?.Tag, r.Attachments, DailyNotePath(r.NoteDate))),
            CaptureMode.Task => SaveTask(text, r),
            CaptureMode.Inbox => SaveInbox(r.Text ?? "", r.Attachments),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    // ---------- daily notes ----------

    public string DailyNotePath(DateTime date)
    {
        string name = MomentFormat.Format(date, _s.DailyFormat);
        return VaultPath(CombineVault(_s.DailyFolder, name + ".md"));
    }

    private string SaveToDaily(DateTime date, string heading, string line)
    {
        string path = DailyNotePath(date);
        bool crlf;
        string content = File.Exists(path) ? ReadNormalized(path, out crlf) : NewDailyNoteContent(date, path, out crlf);
        content = InsertUnderHeading(content, heading, line);
        WriteNormalized(path, content, crlf);
        return path;
    }

    private string NewDailyNoteContent(DateTime date, string notePath, out bool crlf)
    {
        crlf = false;
        if (!_s.UseTemplateForNewDailyNotes || string.IsNullOrWhiteSpace(_s.DailyTemplate)) return "";
        string tpl = ResolveTemplate(_s.DailyTemplate);
        if (tpl == null) return "";
        string title = Path.GetFileNameWithoutExtension(notePath);
        // New notes keep the template's line endings.
        string raw = File.ReadAllText(tpl);
        crlf = raw.Contains("\r\n");
        return RenderTemplate(raw.Replace("\r\n", "\n"), date, title, _now());
    }

    private string ResolveTemplate(string template)
    {
        var candidates = new List<string> { template, template + ".md" };
        var tf = ObsidianVault.ReadTemplatesFolder(_vault);
        if (tf != null) { candidates.Add(CombineVault(tf, template)); candidates.Add(CombineVault(tf, template + ".md")); }
        foreach (var c in candidates)
        {
            var p = VaultPath(c);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Supports Obsidian core template variables: {{title}}, {{date}}, {{time}}, {{date:FMT}}, {{time:FMT}}.</summary>
    public static string RenderTemplate(string tpl, DateTime noteDate, string title, DateTime now)
    {
        tpl = tpl.Replace("{{title}}", title);
        tpl = Regex.Replace(tpl, @"\{\{date(?::([^}]*))?\}\}", m =>
            MomentFormat.Format(noteDate, m.Groups[1].Success ? m.Groups[1].Value : "YYYY-MM-DD"));
        tpl = Regex.Replace(tpl, @"\{\{time(?::([^}]*))?\}\}", m =>
            MomentFormat.Format(now, m.Groups[1].Success ? m.Groups[1].Value : "HH:mm"));
        return tpl;
    }

    /// <summary>
    /// Inserts <paramref name="line"/> at the end of the section under a heading named <paramref name="heading"/>.
    /// If the heading doesn't exist, a "## heading" section is appended to the end of the note.
    /// </summary>
    public static string InsertUnderHeading(string content, string heading, string line)
    {
        var lines = content.Length == 0 ? new List<string>() : content.Split('\n').ToList();
        // A trailing newline yields a final empty element; drop it and restore later.
        bool trailingNewline = lines.Count > 0 && lines[lines.Count - 1] == "";
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);

        string want = Normalize(heading);
        int headIdx = -1, level = 0;
        bool inFence = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("```")) inFence = !inFence;
            if (inFence) continue;
            var m = HeadingRx.Match(lines[i]);
            if (m.Success && Normalize(m.Groups[2].Value) == want)
            {
                headIdx = i; level = m.Groups[1].Length; break;
            }
        }

        if (headIdx < 0)
        {
            while (lines.Count > 0 && lines[lines.Count - 1].Trim() == "") lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0) lines.Add("");
            lines.Add("## " + heading.Trim());
            lines.Add(line);
            return string.Join("\n", lines) + "\n";
        }

        // Find the end of this section: next heading of the same or higher level.
        int end = lines.Count;
        inFence = false;
        for (int i = headIdx + 1; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("```")) inFence = !inFence;
            if (inFence) continue;
            var m = HeadingRx.Match(lines[i]);
            if (m.Success && m.Groups[1].Length <= level) { end = i; break; }
        }

        // Insert right after the last non-blank line in the section.
        int insertAt = end;
        while (insertAt - 1 > headIdx && lines[insertAt - 1].Trim() == "") insertAt--;
        lines.Insert(insertAt, line);

        return string.Join("\n", lines) + "\n";
    }

    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");

    private static string Normalize(string h) => Regex.Replace(h.Trim().TrimStart('#').Trim(), @"\s+", " ").ToLowerInvariant();

    private string BulletLine(string text, string tag, List<string> attachments, string notePath)
    {
        var sb = new StringBuilder("- ");
        sb.Append(OneLine(text));
        if (!string.IsNullOrWhiteSpace(tag))
        {
            string t = tag.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            sb.Append(sb.Length > 2 ? " " : "").Append(t);
        }
        foreach (var link in CopyAttachments(attachments, notePath))
            sb.Append(sb.Length > 2 ? " " : "").Append(link);
        return sb.ToString();
    }

    private static string OneLine(string s) => Regex.Replace(s.Trim(), @"\s*\r?\n\s*", " ");

    // ---------- tasks ----------

    public string EffectiveTaskTag()
    {
        if (_s.AutoDetectTaskTag)
        {
            var gf = ObsidianVault.ReadTasksGlobalFilter(_vault);
            if (gf != null) return gf;
        }
        return _s.TaskTag?.Trim() ?? "";
    }

    private string SaveTask(string text, CaptureRequest r)
    {
        string notePath = VaultPath(EnsureMd(_s.TaskNotePath));
        var sb = new StringBuilder("- [ ] ");
        string tag = EffectiveTaskTag();
        if (tag.Length > 0) sb.Append(tag.StartsWith("#") ? tag : "#" + tag).Append(' ');
        sb.Append(OneLine(text));
        foreach (var link in CopyAttachments(r.Attachments, notePath)) sb.Append(' ').Append(link);
        if (r.StartDate is { } sd) sb.Append(" 🛫 ").Append(sd.ToString("yyyy-MM-dd"));
        if (r.DueDate is { } dd) sb.Append(" 📅 ").Append(dd.ToString("yyyy-MM-dd"));
        string line = sb.ToString().Replace("  ", " ");

        bool exists = File.Exists(notePath);
        bool crlf = exists && File.ReadAllText(notePath).Contains("\r\n");
        string content = exists ? ReadNormalized(notePath, out _) : "";
        content = _s.TasksAtTop ? InsertAtTop(content, line) : AppendLine(content, line);
        WriteNormalized(notePath, content, crlf);
        return notePath;
    }

    /// <summary>Inserts a line at the top of a note, after any YAML frontmatter and a leading H1 title.</summary>
    public static string InsertAtTop(string content, string line)
    {
        var lines = content.Length == 0 ? new List<string>() : content.Split('\n').ToList();
        int i = 0;
        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            int close = lines.FindIndex(1, l => l.Trim() == "---");
            if (close > 0) i = close + 1;
        }
        while (i < lines.Count && lines[i].Trim() == "") i++;
        if (i < lines.Count && Regex.IsMatch(lines[i], @"^#\s"))
        {
            i++;
            while (i < lines.Count && lines[i].Trim() == "") i++;
        }
        lines.Insert(i, line);
        var result = string.Join("\n", lines);
        return result.EndsWith("\n") ? result : result + "\n";
    }

    public static string AppendLine(string content, string line)
    {
        content = content.TrimEnd('\n', ' ', '\t');
        return content.Length == 0 ? line + "\n" : content + "\n" + line + "\n";
    }

    // ---------- inbox ----------

    private string SaveInbox(string raw, List<string> attachments)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        string title = SanitizeFileName(lines[0].Trim());
        if (title.Length == 0) title = "Capture " + _now().ToString("yyyy-MM-dd HHmm");
        string body = string.Join("\n", lines.Skip(1)).Trim('\n');

        string folder = VaultPath(_s.InboxFolder ?? "");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, title + ".md");
        for (int n = 1; File.Exists(path); n++) path = Path.Combine(folder, $"{title} {n}.md");

        var links = CopyAttachments(attachments, path);
        var sb = new StringBuilder(body);
        if (links.Count > 0)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(string.Join("\n", links));
        }
        string content = sb.Length > 0 ? sb.ToString().TrimEnd() + "\n" : "";
        WriteNormalized(path, content, false);
        return path;
    }

    public static string SanitizeFileName(string s)
    {
        s = Regex.Replace(s, @"[\\/:*?""<>|#^\[\]]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.');
        return s.Length > 100 ? s.Substring(0, 100).Trim() : s;
    }

    // ---------- attachments ----------

    /// <summary>Copies files into the vault's attachment folder and returns wiki-links for them.</summary>
    public List<string> CopyAttachments(List<string> files, string notePath)
    {
        var links = new List<string>();
        if (files.Count == 0) return links;

        string folder = AttachmentFolderFor(notePath);
        Directory.CreateDirectory(folder);
        foreach (var src in files)
        {
            if (!File.Exists(src)) continue;
            string name = Path.GetFileNameWithoutExtension(src);
            string ext = Path.GetExtension(src);
            string dest = Path.Combine(folder, name + ext);
            for (int n = 1; File.Exists(dest); n++) dest = Path.Combine(folder, $"{name} {n}{ext}");
            File.Copy(src, dest);
            string rel = RelativePath(_vault, dest).Replace('\\', '/');
            links.Add((_s.EmbedAttachments ? "!" : "") + "[[" + rel + "]]");
        }
        return links;
    }

    public string AttachmentFolderFor(string notePath)
    {
        string setting = string.IsNullOrWhiteSpace(_s.AttachmentFolderOverride)
            ? ObsidianVault.ReadAttachmentFolder(_vault)
            : _s.AttachmentFolderOverride.Trim();

        string noteDir = Path.GetDirectoryName(notePath) ?? _vault;
        if (setting == "/" || setting == "") return _vault;
        if (setting == "./" || setting == ".") return noteDir;
        if (setting.StartsWith("./")) return Path.GetFullPath(Path.Combine(noteDir, setting.Substring(2)));
        return VaultPath(setting);
    }

    // ---------- helpers ----------

    private static string EnsureMd(string p) => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? p : p + ".md";

    private static string CombineVault(string folder, string name)
    {
        folder = (folder ?? "").Trim().Trim('/', '\\');
        return folder.Length == 0 ? name : folder + "/" + name;
    }

    /// <summary>Turns a vault-relative path ("Daily/2026-10-03.md") into a full OS path.</summary>
    public string VaultPath(string rel)
    {
        rel = (rel ?? "").Trim().TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_vault, rel));
    }

    public static string RelativePath(string root, string full)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        full = Path.GetFullPath(full);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
    }

    private static string ReadNormalized(string path, out bool crlf)
    {
        string t = File.ReadAllText(path);
        crlf = t.Contains("\r\n");
        return t.Replace("\r\n", "\n");
    }

    private static void WriteNormalized(string path, string content, bool crlf)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (crlf) content = content.Replace("\n", "\r\n");
        File.WriteAllText(path, content, Utf8NoBom);
    }
}
