using System; using System.IO;
using VaultCapture.Core;
int fails = 0;
void Eq(string name, string exp, string act) { if (exp != act) { fails++; Console.WriteLine($"FAIL {name}\n--- expected ---\n{exp}\n--- actual ---\n{act}"); } else Console.WriteLine($"ok   {name}"); }

var d = new DateTime(2026, 10, 3, 14, 5, 9);
Eq("moment basic", "2026-10-03", MomentFormat.Format(d, "YYYY-MM-DD"));
Eq("moment folders", "2026/10-October/2026-10-03 Saturday", MomentFormat.Format(d, "YYYY/MM-MMMM/YYYY-MM-DD dddd"));
Eq("moment literal", "Week 40 of 2026, 3rd", MomentFormat.Format(d, "[Week] W [of] GGGG, Do"));
Eq("moment time", "02:05 PM", MomentFormat.Format(d, "hh:mm A"));

Eq("heading missing (empty)", "## Captured\n- a\n", CaptureWriter.InsertUnderHeading("", "Captured", "- a"));
Eq("heading missing", "# Day\n\nstuff\n\n## Captured\n- a\n", CaptureWriter.InsertUnderHeading("# Day\n\nstuff\n\n", "Captured", "- a"));
Eq("heading existing w/ items + next section",
   "## Captured\n- x\n- a\n\n## Journal\n- j\n",
   CaptureWriter.InsertUnderHeading("## Captured\n- x\n\n## Journal\n- j\n", "Captured", "- a"));
Eq("subheadings kept inside", "## Log\n### Sub\n- s\n- a\n## Next\n",
   CaptureWriter.InsertUnderHeading("## Log\n### Sub\n- s\n## Next\n", "log", "- a"));
Eq("empty section", "## Wins\n- a\n\n## Other\n", CaptureWriter.InsertUnderHeading("## Wins\n\n## Other\n", "Wins", "- a"));
Eq("ignore code fence", "```\n## Captured\n```\n\n## Captured\n- a\n",
   CaptureWriter.InsertUnderHeading("```\n## Captured\n```\n", "Captured", "- a"));

Eq("top after frontmatter+h1", "---\na: 1\n---\n# Tasks\n\n- [ ] new\n- [ ] old\n",
   CaptureWriter.InsertAtTop("---\na: 1\n---\n# Tasks\n\n- [ ] old\n", "- [ ] new"));
Eq("top empty", "- [ ] new\n", CaptureWriter.InsertAtTop("", "- [ ] new"));
Eq("append", "- [ ] old\n- [ ] new\n", CaptureWriter.AppendLine("- [ ] old\n\n", "- [ ] new"));
Eq("sanitize", "Idea foo bar", CaptureWriter.SanitizeFileName("Idea: foo/bar?"));
Eq("template", "# 2026-10-03\n2026-10-03 Sat 14:05", CaptureWriter.RenderTemplate("# {{title}}\n{{date}} {{date:ddd}} {{time}}", d.Date, "2026-10-03", d));

// End-to-end in a temp vault
var vault = Path.Combine(Path.GetTempPath(), "vc-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
Directory.CreateDirectory(Path.Combine(vault, ".obsidian", "plugins", "obsidian-tasks-plugin"));
Directory.CreateDirectory(Path.Combine(vault, "Templates"));
File.WriteAllText(Path.Combine(vault, ".obsidian", "daily-notes.json"), "{\"folder\":\"Journal/Daily\",\"format\":\"YYYY-MM-DD\",\"template\":\"Templates/Daily\"}");
File.WriteAllText(Path.Combine(vault, ".obsidian", "app.json"), "{\"attachmentFolderPath\":\"Attachments\"}");
File.WriteAllText(Path.Combine(vault, ".obsidian", "plugins", "obsidian-tasks-plugin", "data.json"), "{\"globalFilter\":\"#task\"}");
File.WriteAllText(Path.Combine(vault, "Templates", "Daily.md"), "# {{title}}\r\n\r\n## Journal\r\n\r\n## Gratitude\r\n\r\n## Notes\r\n");
var att = Path.Combine(vault, "..", "pic-" + Guid.NewGuid().ToString("N").Substring(0, 4) + ".png"); File.WriteAllText(att, "x");

var s = new AppSettings();
var dc = ObsidianVault.ReadDailyConfig(vault)!;
s.DailyFolder = dc.Folder; s.DailyFormat = dc.Format; s.DailyTemplate = dc.Template;
var w = new CaptureWriter(s, vault, () => d);
var p1 = w.Save(new CaptureRequest { Mode = CaptureMode.Daily, Text = "first idea", NoteDate = d.Date });
w.Save(new CaptureRequest { Mode = CaptureMode.Journal, Text = "grateful for coffee", NoteDate = d.Date, Journal = s.JournalTypes[1] });
w.Save(new CaptureRequest { Mode = CaptureMode.Journal, Text = "shipped it", NoteDate = d.Date, Journal = s.JournalTypes[2] });
w.Save(new CaptureRequest { Mode = CaptureMode.Daily, Text = "second idea", NoteDate = d.Date, Attachments = { att } });
Eq("daily path", Path.Combine(vault, "Journal", "Daily", "2026-10-03.md"), p1);
Eq("daily note", "# 2026-10-03\n\n## Journal\n\n## Gratitude\n- grateful for coffee #journal/gratitude\n\n## Notes\n\n## Captured\n- first idea\n- second idea ![[Attachments/" + Path.GetFileName(att) + "]]\n\n## Wins\n- shipped it #journal/win\n", File.ReadAllText(p1).Replace("\r\n", "\n"));
Eq("new note keeps template CRLF", "True", File.ReadAllText(p1).Contains("\r\n").ToString());
// CRLF files stay CRLF
var crlf = Path.Combine(vault, "crlf.md"); File.WriteAllText(crlf, "# T\r\n\r\n## Captured\r\n- a\r\n");
Eq("crlf kept", "# T\r\n\r\n## Captured\r\n- a\r\n- b\r\n", CaptureWriter.InsertUnderHeading(File.ReadAllText(crlf).Replace("\r\n","\n"), "Captured", "- b").Replace("\n","\r\n"));
var js = new AppSettings(); js.Save(Path.Combine(vault, "s.json")); var back = AppSettings.Load(Path.Combine(vault, "s.json"));
Eq("settings roundtrip", "3 Ctrl+Shift+Space Wins", back.JournalTypes.Count + " " + back.Hotkey + " " + back.JournalTypes[2].Heading);

var tp = w.Save(new CaptureRequest { Mode = CaptureMode.Task, Text = "Call Bob", StartDate = d.Date, DueDate = d.Date.AddDays(2) });
w.Save(new CaptureRequest { Mode = CaptureMode.Task, Text = "Second task" });
Eq("tasks", "- [ ] #task Second task\n- [ ] #task Call Bob 🛫 2026-10-03 📅 2026-10-05\n", File.ReadAllText(tp));

var ip = w.Save(new CaptureRequest { Mode = CaptureMode.Inbox, Text = "Big idea: thing\nline two\nline three" });
Eq("inbox path", Path.Combine(vault, "Inbox", "Big idea thing.md"), ip);
Eq("inbox body", "line two\nline three\n", File.ReadAllText(ip));
var ip2 = w.Save(new CaptureRequest { Mode = CaptureMode.Inbox, Text = "Big idea: thing" });
Eq("inbox dup", Path.Combine(vault, "Inbox", "Big idea thing 1.md"), ip2);

Console.WriteLine(fails == 0 ? "\nALL PASSED" : $"\n{fails} FAILED");
return fails;
