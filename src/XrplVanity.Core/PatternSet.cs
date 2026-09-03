using System;
using System.Collections.Generic;

namespace XrplVanity.Core;

/// <summary>Matches an address (excluding the leading 'r') against bucketed patterns.</summary>
public sealed class PatternSet
{
    private readonly Bucket[] _starts;
    private readonly Bucket[] _ends;
    private readonly List<string> _contains;
    private readonly List<NormalizedPattern> _unreachable;

    private PatternSet(Bucket[] starts, Bucket[] ends, List<string> contains,
        List<NormalizedPattern> unreachable)
    {
        _starts = starts;
        _ends = ends;
        _contains = contains;
        _unreachable = unreachable;
    }

    public IReadOnlyList<NormalizedPattern> Unreachable => _unreachable;

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

        public Builder Add(MatchPosition position, string pattern, bool ignoreCase)
        {
            NormalizedPattern normalized = PatternNormalizer.Normalize(pattern);
            if (!normalized.Reachable)
            {
                _unreachable.Add(normalized);
                return this;
            }

            foreach (string variant in Variants(normalized.Value, ignoreCase))
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
            return new PatternSet(ToBuckets(_starts), ToBuckets(_ends), _contains, _unreachable);
        }

        private static IEnumerable<string> Variants(string value, bool ignoreCase)
        {
            if (!ignoreCase)
            {
                yield return value;
                yield break;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            string upper = value.ToUpperInvariant();
            string title = value.Length == 0
                ? value
                : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

            foreach (string v in new[] { value, upper, title })
            {
                // patterns are already normalized; re-normalize the case variant so
                // characters like 'L'/'l' stay inside the alphabet
                NormalizedPattern n = PatternNormalizer.Normalize(v);
                if (n.Reachable && seen.Add(n.Value))
                {
                    yield return n.Value;
                }
            }
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
