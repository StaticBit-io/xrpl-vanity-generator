using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XrplVanity.Core;

namespace XrplVanity.Gpu;

/// <summary>
/// The expanded targets of a <see cref="PatternSet"/> flattened for upload: groups keyed by
/// (position, length) in ascending order, each group's targets sorted ordinal and packed at a
/// fixed stride equal to their length, so the device can binary-search a group.
/// </summary>
public sealed class TargetTable
{
    public const uint PositionStarts = 0;
    public const uint PositionEnds = 1;
    public const uint PositionContains = 2;

    private TargetTable(uint[] groups, byte[] chars)
    {
        Groups = groups;
        Chars = chars;
    }

    /// <summary>Four values per group: position, length, offset into <see cref="Chars"/>, count.</summary>
    public uint[] Groups { get; }

    public byte[] Chars { get; }

    public int GroupCount => Groups.Length / 4;

    public static TargetTable Build(PatternSet patterns)
    {
        SortedDictionary<(uint Position, int Length), HashSet<string>> grouped = new();
        foreach ((MatchPosition position, string value) in patterns.EnumerateTargets())
        {
            uint code = position switch
            {
                MatchPosition.Starts => PositionStarts,
                MatchPosition.Ends => PositionEnds,
                _ => PositionContains,
            };
            (uint, int) key = (code, value.Length);
            if (!grouped.TryGetValue(key, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                grouped[key] = set;
            }

            set.Add(value);
        }

        List<uint> groups = new(grouped.Count * 4);
        List<byte> chars = new();
        foreach (KeyValuePair<(uint Position, int Length), HashSet<string>> entry in grouped)
        {
            List<string> sorted = entry.Value.OrderBy(v => v, StringComparer.Ordinal).ToList();
            groups.Add(entry.Key.Position);
            groups.Add((uint)entry.Key.Length);
            groups.Add((uint)chars.Count);
            groups.Add((uint)sorted.Count);
            foreach (string value in sorted)
            {
                chars.AddRange(Encoding.ASCII.GetBytes(value));
            }
        }

        return new TargetTable(groups.ToArray(), chars.ToArray());
    }
}
