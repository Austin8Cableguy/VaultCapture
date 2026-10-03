using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace VaultCapture.Core;

public class VaultEntry
{
    public VaultEntry(string path, long ts, bool open) { Path = path; Ts = ts; Open = open; }
    public string Path { get; }
    public long Ts { get; }
    public bool Open { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public override string ToString() => Name + "  —  " + Path;
}

public class DailyNoteConfig
{
    public DailyNoteConfig(string folder, string format, string template) { Folder = folder; Format = format; Template = template; }
    public string Folder { get; }
    public string Format { get; }
    public string Template { get; }
}

/// <summary>Reads Obsidian's own config files to discover vaults and their settings.</summary>
public static class ObsidianVault
{
    public static string DefaultObsidianJson =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json");

    public static List<VaultEntry> FindVaults(string obsidianJsonPath = null)
    {
        var list = new List<VaultEntry>();
        var root = ReadJson(obsidianJsonPath ?? DefaultObsidianJson) as IDictionary<string, object>;
        var vaults = Get(root, "vaults") as IDictionary<string, object>;
        if (vaults != null)
        {
            foreach (var kv in vaults)
            {
                var v = kv.Value as IDictionary<string, object>;
                var p = Get(v, "path") as string;
                if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) continue;
                long ts = 0;
                try { ts = Convert.ToInt64(Get(v, "ts") ?? 0); } catch { }
                bool open = Get(v, "open") is bool b && b;
                list.Add(new VaultEntry(p, ts, open));
            }
        }
        return list.OrderByDescending(v => v.Open).ThenByDescending(v => v.Ts).ToList();
    }

    /// <summary>The open vault, or else the most recently used one.</summary>
    public static VaultEntry PickDefault(IEnumerable<VaultEntry> vaults) =>
        vaults.OrderByDescending(v => v.Open).ThenByDescending(v => v.Ts).FirstOrDefault();

    private static object ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private static object Get(object obj, string key) =>
        obj is IDictionary<string, object> d && d.TryGetValue(key, out var v) ? v : null;

    private static string Str(object obj, string key) => Get(obj, key) as string;

    /// <summary>Daily note settings from the Periodic Notes plugin (if enabled) or the core Daily Notes plugin.</summary>
    public static DailyNoteConfig ReadDailyConfig(string vault)
    {
        var obs = Path.Combine(vault, ".obsidian");

        var community = ReadJson(Path.Combine(obs, "community-plugins.json")) as IEnumerable;
        bool periodicEnabled = community != null && community.Cast<object>().Any(x => (x as string) == "periodic-notes");
        if (periodicEnabled)
        {
            var daily = Get(ReadJson(Path.Combine(obs, "plugins", "periodic-notes", "data.json")), "daily");
            if (Get(daily, "enabled") is bool en && en)
                return new DailyNoteConfig(Str(daily, "folder") ?? "", NonEmpty(Str(daily, "format")) ?? "YYYY-MM-DD", Str(daily, "template") ?? "");
        }

        var core = ReadJson(Path.Combine(obs, "daily-notes.json"));
        if (core == null) return null;
        return new DailyNoteConfig(Str(core, "folder") ?? "", NonEmpty(Str(core, "format")) ?? "YYYY-MM-DD", Str(core, "template") ?? "");
    }

    /// <summary>Obsidian's "Default location for new attachments" (attachmentFolderPath in app.json).</summary>
    public static string ReadAttachmentFolder(string vault) =>
        Str(ReadJson(Path.Combine(vault, ".obsidian", "app.json")), "attachmentFolderPath") ?? "/";

    /// <summary>Global filter tag from the Tasks plugin, e.g. "#task", or null.</summary>
    public static string ReadTasksGlobalFilter(string vault)
    {
        var f = Str(ReadJson(Path.Combine(vault, ".obsidian", "plugins", "obsidian-tasks-plugin", "data.json")), "globalFilter");
        return string.IsNullOrWhiteSpace(f) ? null : f.Trim();
    }

    /// <summary>Templates folder of the core Templates plugin (used to resolve bare template names).</summary>
    public static string ReadTemplatesFolder(string vault) =>
        NonEmpty(Str(ReadJson(Path.Combine(vault, ".obsidian", "templates.json")), "folder"));

    private static string NonEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
