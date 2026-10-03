# VaultCapture

**Quick capture for Obsidian on Windows.** Press a hotkey from any app, type a thought, press Enter, and it lands in the right place in your Obsidian vault. Obsidian doesn't need to be open.

VaultCapture is an independent, unofficial tool. It isn't made by or affiliated with Obsidian.

## Credit

The idea for this app comes from **Practical PKM's QuickCapture**, a Mac menu bar app for capturing straight into an Obsidian vault ([quickcapture.practicalpkm.com](https://quickcapture.practicalpkm.com)). That app is Mac-only, so VaultCapture brings the same workflow to Windows. VaultCapture's code is written from scratch; it shares no code with the original. If you're on a Mac, use the original, and check out [Practical PKM](https://practicalpkm.com) for more Obsidian workflows.

## Features

- **Global hotkey** (default `Ctrl+Shift+Space`) opens a small capture window over whatever you're doing
- **Daily note**: adds `- text` under a `## Captured` heading in today's note (or any date you pick)
- **Journal entries**: Journal, Gratitude and Win by default, each with its own heading and tag; add your own
- **Tasks**: `- [ ] #task text 🛫 start 📅 due` in a master task note, in the Obsidian Tasks plugin's emoji format
- **Inbox notes**: a new note in your Inbox folder; the first line is the title
- **Attachments**: attach or drag in files; they're copied to your vault's attachment folder and embedded
- **Zero setup**: finds your vaults and reads your daily note, attachment and Tasks plugin settings from Obsidian
- **Light and dark themes**, or follow Windows
- **No install, no runtime**: one ~170 KB `.exe` using .NET Framework 4.8, which is built into Windows 10 and 11
- **Private**: no network access, no telemetry. It only reads and writes files in your vault

## Install

1. Download `VaultCapture.exe` from the [latest release](../../releases/latest).
2. Put it somewhere permanent (for example `C:\Users\<you>\Apps\VaultCapture\`) and double-click it.
3. If Windows SmartScreen says "Windows protected your PC", click **More info → Run anyway**. The app isn't code-signed yet.
4. Look for the icon in the system tray (check the `^` overflow by the clock). Right-click it for Settings and **Start with Windows**.

## Using it

| Keys | Action |
|---|---|
| `Ctrl+Shift+Space` | Open the capture window (or click the tray icon) |
| `Enter` | Save |
| `Esc` | Close (your draft is kept) |
| `Shift+Enter` | New line (Inbox mode) |
| `Ctrl+D` / `Ctrl+J` / `Ctrl+G` / `Ctrl+W` / `Ctrl+T` / `Ctrl+I` | Daily / Journal / Gratitude / Win / Task / Inbox |
| `Ctrl+O` | Attach files (or drag files onto the window) |
| `Ctrl+,` | Settings |

Click the calendar icon to change the daily note date. In Task mode, the airplane sets a start date and the calendar a due date; right-click either to clear it.

## Settings

Stored in `%APPDATA%\VaultCapture\settings.json`. Everything is editable in the Settings window:
vault, hotkey, theme, daily note folder/format/template (imported from your vault at startup), the "Captured" heading,
journal types (label, heading, tag, shortcut letter), master task note, top/bottom placement, task tag,
inbox folder, attachment folder, and embed vs link.

Daily note templates support `{{title}}`, `{{date}}`, `{{time}}` and `{{date:FORMAT}}`. Templater code is copied as-is and isn't run.

## Troubleshooting

Run `VaultCapture.exe --selftest` (or double-click `Run-SelfTest.bat` from the release). It checks your setup,
tests note writing in a throwaway vault (never your real one), and saves screenshots plus a `report.txt`
to a `selftest-results` folder next to the exe. Please attach that report to bug reports.

## Building from source

The app targets .NET Framework 4.8 and needs only the C# compiler.

- **Linux/macOS**: install the .NET SDK and Mono reference assemblies, then run `src/build.sh`.
- **Windows**: from a Developer Command Prompt (Visual Studio or Build Tools):
  ```
  cd src
  csc -target:winexe -langversion:latest -optimize+ -out:..\dist\VaultCapture.exe -win32icon:app.ico -win32manifest:app.manifest -resource:app.ico,VaultCapture.app.ico -r:System.Web.Extensions.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll Core\*.cs App\*.cs
  ```

Unit tests for the note-writing logic live in `tests/` (`tests/build.sh /tmp/tests.exe exe tests/Program.cs src/Core/*.cs`, then run it).

## License

[MIT](LICENSE)
