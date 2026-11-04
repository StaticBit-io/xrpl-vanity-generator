using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Storage;

/// <summary>
/// One saved pattern chip. <paramref name="EndsText"/> is the second half of a
/// <c>MatchPosition.Pair</c> draft and null for every other position; it is trailing and optional
/// so a settings file written before paired patterns existed still deserializes, and a Pair draft
/// that arrives without it is dropped on restore rather than rebuilt as half a rule.
/// <paramref name="NearStart"/> marks a Pair draft whose starts half may begin at the 2nd or 3rd
/// character; trailing and optional for the same reason.
/// </summary>
public sealed record PatternDraft(string Text, string Position, bool IgnoreCase, string? EndsText = null, bool NearStart = false);

public sealed record AppSettings
{
    public IReadOnlyList<PatternDraft> Patterns { get; init; } = Array.Empty<PatternDraft>();
    public IReadOnlyList<string> Targets { get; init; } = new[] { "cpu" };
    public int CpuThreads { get; init; } = Math.Max(1, Environment.ProcessorCount);
    public int DutyPercent { get; init; } = 100;
    public int? Count { get; init; }
    public int WindowWidth { get; init; } = 1100;
    public int WindowHeight { get; init; } = 720;
    public CloseBehavior CloseBehavior { get; init; } = CloseBehavior.Ask;
    public bool NotificationsEnabled { get; init; } = true;
    public int ClipboardClearSeconds { get; init; } = 30;
    public int SeedRevealSeconds { get; init; } = 15;
    public IReadOnlyDictionary<string, double> RateHints { get; init; } = new Dictionary<string, double>();

    public bool Equals(AppSettings? other) =>
        other is not null
        && Patterns.SequenceEqual(other.Patterns)
        && Targets.SequenceEqual(other.Targets)
        && CpuThreads == other.CpuThreads && DutyPercent == other.DutyPercent && Count == other.Count
        && WindowWidth == other.WindowWidth && WindowHeight == other.WindowHeight
        && CloseBehavior == other.CloseBehavior && NotificationsEnabled == other.NotificationsEnabled
        && ClipboardClearSeconds == other.ClipboardClearSeconds && SeedRevealSeconds == other.SeedRevealSeconds
        && RateHints.Count == other.RateHints.Count && RateHints.All(kv => other.RateHints.TryGetValue(kv.Key, out double v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(CpuThreads, DutyPercent, Count, WindowWidth, WindowHeight, CloseBehavior);
}
