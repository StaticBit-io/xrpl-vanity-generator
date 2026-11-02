using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XrplVanity.Core;

namespace XrplVanity.Gpu;

/// <summary>
/// The expanded targets of a <see cref="PatternSet"/> flattened for upload: the plain positions
/// first, keyed by (position, length) in ascending order, then one group per distinct half of a
/// paired rule in the order the rules were added. Each group's targets are sorted ordinal and
/// packed at a fixed stride equal to their length, so the device can binary-search a group.
/// </summary>
public sealed class TargetTable
{
    public const uint PositionStarts = 0;
    public const uint PositionEnds = 1;
    public const uint PositionContains = 2;
    public const uint PositionNearStart = 3;

    // Pair halves sit above every plain position: the device's or-loop skips any code from
    // PositionPairStarts up, so a half can never match on its own.
    public const uint PositionPairStarts = 4;
    public const uint PositionPairEnds = 5;

    private TargetTable(uint[] groups, byte[] chars, uint[] pairs)
    {
        Groups = groups;
        Chars = chars;
        Pairs = pairs;
    }

    /// <summary>Four values per group: position, length, offset into <see cref="Chars"/>, count.</summary>
    public uint[] Groups { get; }

    public byte[] Chars { get; }

    /// <summary>
    /// Two group indices per pair rule: the index of its starts-half group followed by the
    /// index of its ends-half group, in the order the rules appear in
    /// <see cref="PatternSet.Pairs"/>.
    /// </summary>
    public uint[] Pairs { get; }

    public int GroupCount => Groups.Length / 4;

    public int PairCount => Pairs.Length / 2;

    public static TargetTable Build(PatternSet patterns)
    {
        SortedDictionary<(uint Position, int Length), HashSet<string>> grouped = new();
        foreach ((MatchPosition position, string value) in patterns.EnumerateTargets())
        {
            uint code = position switch
            {
                MatchPosition.Starts => PositionStarts,
                MatchPosition.Ends => PositionEnds,
                MatchPosition.Contains => PositionContains,
                MatchPosition.NearStart => PositionNearStart,
                // A pair half must never reach the or-loop as a plain group; EnumerateTargets
                // does not yield one, and this fails closed if that ever changes.
                _ => throw new ArgumentOutOfRangeException(nameof(patterns), position,
                    "EnumerateTargets yielded a position the device table cannot represent."),
            };
            (uint, int) key = (code, value.Length);
            if (!grouped.TryGetValue(key, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                grouped[key] = set;
            }

            set.Add(value);
        }

        // Pair halves cannot merge into the plain (position, length) groups above: a half is
        // identified by its group index plus the exact variants inside it, and the device
        // matches a pair by intersecting one starts-group hit with one ends-group hit. Keying
        // purely on (position, length) would union the variants of two rules that happen to
        // share a half's length but not its content, letting a wrong combination of halves
        // match. Correctness first: a rule's half gets its own group unless another rule's half
        // is byte-identical (same length and the same set of variants), in which case they share
        // one group. Keying on the rule index instead would prevent that legitimate sharing.
        Dictionary<(uint Position, string SortedVariants), List<string>> pairGroups = new();
        List<((uint Position, string SortedVariants) StartsKey, (uint Position, string SortedVariants) EndsKey)> pairKeys = new(patterns.Pairs.Count);
        foreach (PatternSet.Pair pair in patterns.Pairs)
        {
            (uint Position, string SortedVariants) startsKey = PairHalfKey(PositionPairStarts, pair.Starts);
            (uint Position, string SortedVariants) endsKey = PairHalfKey(PositionPairEnds, pair.Ends);

            if (!pairGroups.ContainsKey(startsKey))
            {
                pairGroups[startsKey] = pair.Starts.OrderBy(v => v, StringComparer.Ordinal).ToList();
            }

            if (!pairGroups.ContainsKey(endsKey))
            {
                pairGroups[endsKey] = pair.Ends.OrderBy(v => v, StringComparer.Ordinal).ToList();
            }

            pairKeys.Add((startsKey, endsKey));
        }

        List<uint> groups = new(grouped.Count * 4 + pairGroups.Count * 4);
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

        // The pairs index below stores real group indices, so the group list must be final
        // before any pair-half group index is recorded.
        Dictionary<(uint Position, string SortedVariants), int> pairGroupIndex = new(pairGroups.Count);
        foreach (KeyValuePair<(uint Position, string SortedVariants), List<string>> entry in pairGroups)
        {
            pairGroupIndex[entry.Key] = groups.Count / 4;
            groups.Add(entry.Key.Position);
            groups.Add((uint)entry.Value[0].Length);
            groups.Add((uint)chars.Count);
            groups.Add((uint)entry.Value.Count);
            foreach (string value in entry.Value)
            {
                chars.AddRange(Encoding.ASCII.GetBytes(value));
            }
        }

        uint[] pairs = new uint[pairKeys.Count * 2];
        for (int i = 0; i < pairKeys.Count; i++)
        {
            pairs[i * 2] = (uint)pairGroupIndex[pairKeys[i].StartsKey];
            pairs[i * 2 + 1] = (uint)pairGroupIndex[pairKeys[i].EndsKey];
        }

        return new TargetTable(groups.ToArray(), chars.ToArray(), pairs);
    }

    /// <summary>
    /// Identifies a pair half by its position code and the ordinal-sorted, '\0'-joined variant
    /// list, so two halves compare equal only when they carry byte-identical variants.
    /// </summary>
    private static (uint Position, string SortedVariants) PairHalfKey(uint position, IReadOnlyCollection<string> variants)
    {
        string sortedVariants = string.Join('\0', variants.OrderBy(v => v, StringComparer.Ordinal));
        return (position, sortedVariants);
    }
}
