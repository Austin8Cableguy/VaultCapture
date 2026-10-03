using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VaultCapture.App;

/// <summary>Parses/formats hotkey strings like "Ctrl+Shift+Space".</summary>
internal static class Hotkey
{
    public static bool TryParse(string text, out Keys keys)
    {
        keys = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        Keys mods = Keys.None, key = Keys.None;
        foreach (var raw in text.Split('+'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Keys.Control; continue;
                case "alt": mods |= Keys.Alt; continue;
                case "shift": mods |= Keys.Shift; continue;
                case "win": case "windows": continue; // tracked via HasWin()
            }
            if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
            if (!Enum.TryParse(p, true, out key)) return false;
        }
        if (key == Keys.None) return false;
        keys = key | mods;
        return true;
    }

    public static string Format(Keys keys)
    {
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (keys.HasFlag(Keys.Shift)) parts.Add("Shift");
        var k = keys & Keys.KeyCode;
        if (k == Keys.None || k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu) return string.Join("+", parts) + (parts.Count > 0 ? "+" : "");
        string name = k.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name.Substring(1);
        parts.Add(name);
        return string.Join("+", parts);
    }

    /// <summary>Converts a Keys value (with modifiers) to RegisterHotKey arguments.</summary>
    public static void ToNative(Keys keys, bool win, out uint mods, out uint vk)
    {
        mods = Native.MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control)) mods |= Native.MOD_CONTROL;
        if (keys.HasFlag(Keys.Alt)) mods |= Native.MOD_ALT;
        if (keys.HasFlag(Keys.Shift)) mods |= Native.MOD_SHIFT;
        if (win) mods |= Native.MOD_WIN;
        vk = (uint)(keys & Keys.KeyCode);
    }

    public static bool HasWin(string text) =>
        text != null && text.Split('+').Any(p => p.Trim().Equals("win", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Hidden window that receives WM_HOTKEY for a system-wide shortcut.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int Id = 0xC0DE;
    private bool _registered;
    public event Action Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public bool Register(string hotkeyText)
    {
        Unregister();
        if (!Hotkey.TryParse(hotkeyText, out var keys)) return false;
        Hotkey.ToNative(keys, Hotkey.HasWin(hotkeyText), out var mods, out var vk);
        _registered = Native.RegisterHotKey(Handle, Id, mods, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) Native.UnregisterHotKey(Handle, Id);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
