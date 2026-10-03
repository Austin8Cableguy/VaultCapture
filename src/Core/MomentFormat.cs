using System;
using System.Globalization;
using System.Text;

namespace VaultCapture.Core;

/// <summary>
/// Formats a date using a Moment.js-style format string (the syntax Obsidian uses
/// for daily note names, e.g. "YYYY-MM-DD" or "YYYY/MM/YYYY-MM-DD dddd").
/// </summary>
public static class MomentFormat
{
    private static readonly string[] Tokens =
    {
        "YYYY", "GGGG", "gggg", "YY", "Y",
        "MMMM", "MMM", "MM", "Mo", "M",
        "DDDD", "DDD", "Do", "DD", "D",
        "dddd", "ddd", "dd", "do", "d", "E", "e",
        "WW", "Wo", "W", "ww", "wo", "w",
        "HH", "H", "hh", "h", "kk", "k", "mm", "m", "ss", "s",
        "A", "a", "Q", "X", "x",
    };

    public static string Format(DateTime date, string format)
    {
        if (string.IsNullOrEmpty(format)) format = "YYYY-MM-DD";
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int i = 0;
        while (i < format.Length)
        {
            char c = format[i];
            if (c == '[')
            {
                int end = format.IndexOf(']', i + 1);
                if (end < 0) { sb.Append(format, i + 1, format.Length - i - 1); break; }
                sb.Append(format, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }

            string tok = null;
            foreach (var t in Tokens)
            {
                if (string.CompareOrdinal(format, i, t, 0, t.Length) == 0) { tok = t; break; }
            }

            if (tok == null) { sb.Append(c); i++; continue; }

            int isoWeek = IsoWeek(date);
            sb.Append(tok switch
            {
                "YYYY" => date.Year.ToString("0000", ci),
                "Y" => date.Year.ToString(ci),
                "YY" => (date.Year % 100).ToString("00", ci),
                "GGGG" or "gggg" => IsoYear(date).ToString("0000", ci),
                "MMMM" => date.ToString("MMMM", ci),
                "MMM" => date.ToString("MMM", ci),
                "MM" => date.Month.ToString("00", ci),
                "Mo" => Ordinal(date.Month),
                "M" => date.Month.ToString(ci),
                "DDDD" => date.DayOfYear.ToString("000", ci),
                "DDD" => date.DayOfYear.ToString(ci),
                "Do" => Ordinal(date.Day),
                "DD" => date.Day.ToString("00", ci),
                "D" => date.Day.ToString(ci),
                "dddd" => date.ToString("dddd", ci),
                "ddd" => date.ToString("ddd", ci),
                "dd" => date.ToString("ddd", ci).Substring(0, 2),
                "do" => Ordinal((int)date.DayOfWeek),
                "d" or "e" => ((int)date.DayOfWeek).ToString(ci),
                "E" => (date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek).ToString(ci),
                "WW" or "ww" => isoWeek.ToString("00", ci),
                "Wo" or "wo" => Ordinal(isoWeek),
                "W" or "w" => isoWeek.ToString(ci),
                "HH" => date.Hour.ToString("00", ci),
                "H" => date.Hour.ToString(ci),
                "kk" => (date.Hour == 0 ? 24 : date.Hour).ToString("00", ci),
                "k" => (date.Hour == 0 ? 24 : date.Hour).ToString(ci),
                "hh" => (date.Hour % 12 == 0 ? 12 : date.Hour % 12).ToString("00", ci),
                "h" => (date.Hour % 12 == 0 ? 12 : date.Hour % 12).ToString(ci),
                "mm" => date.Minute.ToString("00", ci),
                "m" => date.Minute.ToString(ci),
                "ss" => date.Second.ToString("00", ci),
                "s" => date.Second.ToString(ci),
                "A" => date.Hour < 12 ? "AM" : "PM",
                "a" => date.Hour < 12 ? "am" : "pm",
                "Q" => ((date.Month - 1) / 3 + 1).ToString(ci),
                "X" => new DateTimeOffset(date).ToUnixTimeSeconds().ToString(ci),
                "x" => new DateTimeOffset(date).ToUnixTimeMilliseconds().ToString(ci),
                _ => tok,
            });
            i += tok.Length;
        }
        return sb.ToString();
    }

    public static int IsoWeek(DateTime d)
    {
        var day = d.DayOfWeek;
        if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday) d = d.AddDays(3);
        return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }

    public static int IsoYear(DateTime d) => d.AddDays(3 - ((int)d.DayOfWeek + 6) % 7).Year;

    private static string Ordinal(int n)
    {
        int mod100 = n % 100;
        string suffix = (mod100 is >= 11 and <= 13) ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n + suffix;
    }
}
