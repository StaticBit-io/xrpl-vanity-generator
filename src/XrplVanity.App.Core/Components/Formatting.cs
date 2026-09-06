using System.Globalization;

namespace XrplVanity.App.Core.Components;

public static class Formatting
{
    public static string Candidates(double value)
    {
        if (value >= 1e21) return value.ToString("0.#e0", CultureInfo.InvariantCulture);
        if (value >= 1e18) return (value / 1e18).ToString("0.#", CultureInfo.InvariantCulture) + " E";
        if (value >= 1e15) return (value / 1e15).ToString("0.#", CultureInfo.InvariantCulture) + " P";
        if (value >= 1e12) return (value / 1e12).ToString("0.#", CultureInfo.InvariantCulture) + " T";
        if (value >= 1e9) return (value / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " G";
        if (value >= 1e6) return (value / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " M";
        return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>A first tick with zero elapsed time divides into infinity — show a dash, not "∞ G/s".</summary>
    public static string Rate(double perSecond) => double.IsFinite(perSecond) ? Candidates(perSecond) + "/s" : "—";

    public static string Duration(TimeSpan? value)
    {
        if (value is not TimeSpan t) return "—";
        if (t.TotalDays > 365) return "> 1 year";
        if (t.TotalDays >= 1) return t.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + " days";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)Math.Round(t.TotalMinutes)} min";
        double seconds = Math.Round(t.TotalSeconds);
        if (t > TimeSpan.Zero && seconds < 1) return "< 1 s";
        return $"{(int)seconds} s";
    }
}
