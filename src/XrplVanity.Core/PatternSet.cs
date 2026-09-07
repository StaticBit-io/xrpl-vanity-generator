using System;
using System.Collections.Generic;
using System.Linq;

namespace XrplVanity.Core;

/// <summary>Matches an address (excluding the leading 'r') against bucketed patterns.</summary>
public sealed class PatternSet
{
    /// <summary>
    /// Longest pattern for which case-insensitive matching expands into every reachable
    /// case combination (up to 2^length of them). Longer patterns are still searched, but
    /// case-insensitivity is dropped for them to avoid an exponential number of variants.
    /// </summary>
    public const int MaxIgnoreCaseLength = 12;

    private readonly Bucket[] _starts;
    private readonly Bucket[] _ends;
    private readonly List<string> _contains;
    private readonly List<Pair> _pairs;
    private readonly List<NormalizedPattern> _unreachable;
    private readonly List<NormalizedPattern> _caseFoldingSkipped;

    private PatternSet(Bucket[] starts, Bucket[] ends, List<string> contains, List<Pair> pairs,
        List<NormalizedPattern> unreachable, List<NormalizedPattern> caseFoldingSkipped)
    {
        _starts = starts;
        _ends = ends;
        _contains = contains;
        _pairs = pairs;
        _unreachable = unreachable;
        _caseFoldingSkipped = caseFoldingSkipped;
    }

    public IReadOnlyList<NormalizedPattern> Unreachable => _unreachable;

    /// <summary>
    /// Patterns for which case-insensitive matching was requested but dropped because the
    /// pattern is longer than <see cref="MaxIgnoreCaseLength"/>; they are still searched
    /// case-sensitively.
    /// </summary>
    public IReadOnlyList<NormalizedPattern> CaseFoldingSkipped => _caseFoldingSkipped;

    /// <summary>True when at least one reachable pattern remains to search for.</summary>
    public bool HasPatterns =>
        _starts.Length > 0 || _ends.Length > 0 || _contains.Count > 0 || _pairs.Count > 0;

    /// <summary>
    /// One rule that requires both halves at once: the body must start with one of
    /// <see cref="Starts"/> and end with one of <see cref="Ends"/>. Each half holds the
    /// expanded, normalized variants of that half only, so case folding stays additive
    /// (4 + 4 variants for "ab" + "yz", not 16 combinations).
    /// </summary>
    public sealed class Pair
    {
        internal Pair(HashSet<string> starts, HashSet<string> ends, string display)
        {
            StartsSet = starts;
            EndsSet = ends;
            StartsLookup = starts.GetAlternateLookup<ReadOnlySpan<char>>();
            EndsLookup = ends.GetAlternateLookup<ReadOnlySpan<char>>();
            StartsLength = starts.First().Length;
            EndsLength = ends.First().Length;
            Display = display;
        }

        internal HashSet<string> StartsSet { get; }
        internal HashSet<string> EndsSet { get; }
        internal HashSet<string>.AlternateLookup<ReadOnlySpan<char>> StartsLookup { get; }
        internal HashSet<string>.AlternateLookup<ReadOnlySpan<char>> EndsLookup { get; }

        public IReadOnlyCollection<string> Starts => StartsSet;
        public IReadOnlyCollection<string> Ends => EndsSet;
        public int StartsLength { get; }
        public int EndsLength { get; }
        public string Display { get; }
    }

    public IReadOnlyList<Pair> Pairs => _pairs;

    /// <summary>
    /// Every expanded target string with its position — exactly the set <see cref="TryMatch"/>
    /// checks. Consumers that match elsewhere (the GPU) build their structures from this so the
    /// Builder stays the single source of truth for normalization and case expansion.
    /// </summary>
    public IEnumerable<(MatchPosition Position, string Value)> EnumerateTargets()
    {
        foreach (Bucket bucket in _starts)
        {
            foreach (string value in bucket.Values)
            {
                yield return (MatchPosition.Starts, value);
            }
        }

        foreach (Bucket bucket in _ends)
        {
            foreach (string value in bucket.Values)
            {
                yield return (MatchPosition.Ends, value);
            }
        }

        foreach (string value in _contains)
        {
            yield return (MatchPosition.Contains, value);
        }
    }

    public bool TryMatch(ReadOnlySpan<char> address, out MatchPosition position, out string pattern)
    {
        ReadOnlySpan<char> body = address[1..]; // drop leading 'r'

        foreach (Pair pair in _pairs)
        {
            // Halves may touch but must not overlap.
            if (pair.StartsLength + pair.EndsLength > body.Length)
            {
                continue;
            }

            if (pair.StartsLookup.Contains(body[..pair.StartsLength])
                && pair.EndsLookup.Contains(body[^pair.EndsLength..]))
            {
                position = MatchPosition.Pair;
                pattern = pair.Display;
                return true;
            }
        }

        if (TryMatchAffix(_starts, body, fromStart: true, out pattern))
        {
            position = MatchPosition.Starts;
            return true;
        }

        if (TryMatchAffix(_ends, body, fromStart: false, out pattern))
        {
            position = MatchPosition.Ends;
            return true;
        }

        foreach (string needle in _contains)
        {
            if (body.IndexOf(needle) >= 0)
            {
                position = MatchPosition.Contains;
                pattern = needle;
                return true;
            }
        }

        position = default;
        pattern = string.Empty;
        return false;
    }

    private static bool TryMatchAffix(Bucket[] buckets, ReadOnlySpan<char> body, bool fromStart,
        out string pattern)
    {
        foreach (Bucket bucket in buckets)
        {
            if (bucket.Length > body.Length)
            {
                continue;
            }

            ReadOnlySpan<char> slice = fromStart
                ? body[..bucket.Length]
                : body[^bucket.Length..];

            if (bucket.Lookup.Contains(slice))
            {
                pattern = new string(slice);
                return true;
            }
        }

        pattern = string.Empty;
        return false;
    }

    private readonly struct Bucket
    {
        public Bucket(int length, HashSet<string> values)
        {
            Length = length;
            Values = values;
            Lookup = values.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public int Length { get; }
        public HashSet<string> Values { get; }
        public HashSet<string>.AlternateLookup<ReadOnlySpan<char>> Lookup { get; }
    }

    public sealed class Builder
    {
        private readonly Dictionary<int, HashSet<string>> _starts = new();
        private readonly Dictionary<int, HashSet<string>> _ends = new();
        private readonly List<string> _contains = new();
        private readonly List<Pair> _pairs = new();
        private readonly List<NormalizedPattern> _unreachable = new();
        private readonly List<NormalizedPattern> _caseFoldingSkipped = new();

        public Builder Add(MatchPosition position, string pattern, bool ignoreCase)
        {
            NormalizedPattern normalized = PatternNormalizer.Normalize(pattern);
            if (!normalized.Reachable)
            {
                _unreachable.Add(normalized);
                return this;
            }

            if (normalized.Value.Length == 0)
            {
                // An empty pattern would match every address; treat it as unreachable so it is
                // reported and skipped rather than turning the search into a match-everything.
                _unreachable.Add(normalized);
                return this;
            }

            bool expandCase = ignoreCase && normalized.Value.Length <= MaxIgnoreCaseLength;
            if (ignoreCase && !expandCase)
            {
                _caseFoldingSkipped.Add(normalized);
            }

            IEnumerable<string> variants = expandCase
                ? CaseVariants(normalized.Value)
                : new[] { normalized.Value };

            foreach (string variant in variants)
            {
                switch (position)
                {
                    case MatchPosition.Starts:
                        AddToBuckets(_starts, variant);
                        break;
                    case MatchPosition.Ends:
                        AddToBuckets(_ends, variant);
                        break;
                    case MatchPosition.Contains:
                        _contains.Add(variant);
                        break;
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a rule that requires both halves. Either half unreachable or empty after
        /// normalization makes the whole rule unreachable; case folding is applied and capped
        /// per half exactly as it is for a single pattern.
        /// </summary>
        public Builder AddPair(string starts, string ends, bool ignoreCase)
        {
            // Display is built from what the user typed (trimmed), not the normalized halves,
            // so the reported rule reads back the way they entered it.
            string display = $"{starts.Trim()}..{ends.Trim()}";
            NormalizedPattern left = PatternNormalizer.Normalize(starts.Trim());
            NormalizedPattern right = PatternNormalizer.Normalize(ends.Trim());

            if (!left.Reachable || !right.Reachable || left.Value.Length == 0 || right.Value.Length == 0)
            {
                _unreachable.Add(new NormalizedPattern($"{left.Value}..{right.Value}", false, display));
                return this;
            }

            HashSet<string> leftSet = ExpandHalf(left, ignoreCase);
            HashSet<string> rightSet = ExpandHalf(right, ignoreCase);
            _pairs.Add(new Pair(leftSet, rightSet, display));
            return this;
        }

        /// <summary>Case variants of one already-normalized half, capped like a single pattern.</summary>
        private HashSet<string> ExpandHalf(NormalizedPattern normalized, bool ignoreCase)
        {
            bool expand = ignoreCase && normalized.Value.Length <= MaxIgnoreCaseLength;
            if (ignoreCase && !expand)
            {
                _caseFoldingSkipped.Add(normalized);
            }

            HashSet<string> set = new(StringComparer.Ordinal);
            foreach (string variant in expand ? CaseVariants(normalized.Value) : new[] { normalized.Value })
            {
                set.Add(variant);
            }

            return set;
        }

        public PatternSet Build()
        {
            return new PatternSet(ToBuckets(_starts), ToBuckets(_ends), _contains, _pairs,
                _unreachable, _caseFoldingSkipped);
        }

        /// <summary>
        /// Every reachable case combination of an already-normalized, reachable pattern.
        /// A two-case letter contributes both cases; a letter that exists in only one case
        /// (i, o, and l→L) or a digit contributes a single choice, so the count is
        /// 2^(two-case letters), not 2^length. The Cartesian product is bounded by
        /// <see cref="MaxIgnoreCaseLength"/> (at most 2^12 combinations).
        /// </summary>
        private static IEnumerable<string> CaseVariants(string value)
        {
            List<string> result = new() { string.Empty };
            foreach (char c in value)
            {
                char lower = PatternNormalizer.NormalizeChar(char.ToLowerInvariant(c));
                char upper = PatternNormalizer.NormalizeChar(char.ToUpperInvariant(c));

                List<string> next = new(result.Count * 2);
                foreach (string prefix in result)
                {
                    next.Add(prefix + lower);
                    if (upper != lower)
                    {
                        next.Add(prefix + upper);
                    }
                }

                result = next;
            }

            return result;
        }

        private static void AddToBuckets(Dictionary<int, HashSet<string>> buckets, string value)
        {
            if (!buckets.TryGetValue(value.Length, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                buckets[value.Length] = set;
            }

            set.Add(value);
        }

        private static Bucket[] ToBuckets(Dictionary<int, HashSet<string>> buckets)
        {
            List<Bucket> list = new(buckets.Count);
            foreach (KeyValuePair<int, HashSet<string>> kv in buckets)
            {
                list.Add(new Bucket(kv.Key, kv.Value));
            }

            return list.ToArray();
        }
    }
}
