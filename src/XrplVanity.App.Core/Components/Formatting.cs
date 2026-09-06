using System.Globalization;

namespace XrplVanity.App.Core.Components;

public static class Formatting
{
    public static string Candidates(double value)
    {
        if (value >= 1e9) return (value / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " G";
        if (value >= 1e6) return (value / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " M";
        return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string Rate(double perSecond) => Candidates(perSecond) + "/s";

    public static string Duration(TimeSpan? value)
    {
        if (value is not TimeSpan t) return "—";
        if (t.TotalDays > 365) return "> 1 year";
        if (t.TotalDays >= 1) return t.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + " days";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)Math.Round(t.TotalMinutes)} min";
        return $"{(int)Math.Round(t.TotalSeconds)} s";
    }
}
