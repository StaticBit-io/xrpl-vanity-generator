using System;
using System.Collections.Generic;

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
    private readonly List<NormalizedPattern> _unreachable;
    private readonly List<NormalizedPattern> _caseFoldingSkipped;

    private PatternSet(Bucket[] starts, Bucket[] ends, List<string> contains,
        List<NormalizedPattern> unreachable, List<NormalizedPattern> caseFoldingSkipped)
    {
        _starts = starts;
        _ends = ends;
        _contains = contains;
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
    public bool HasPatterns => _starts.Length > 0 || _ends.Length > 0 || _contains.Count > 0;

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

        public PatternSet Build()
        {
            return new PatternSet(ToBuckets(_starts), ToBuckets(_ends), _contains, _unreachable,
                _caseFoldingSkipped);
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
