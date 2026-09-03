using System;

namespace XrplVanity.Core;

/// <summary>
/// Evaluates one entropy value: derives its address and reports a <see cref="Match"/>
/// if any pattern hits. Not thread-safe (owns a deriver); use one per worker thread.
/// </summary>
public sealed class CandidateEvaluator
{
    private readonly IAddressDeriver _deriver;
    private readonly PatternSet _patterns;

    public CandidateEvaluator(IAddressDeriver deriver, PatternSet patterns)
    {
        _deriver = deriver;
        _patterns = patterns;
    }

    public bool TryEvaluate(ReadOnlySpan<byte> entropy16, out Match match)
    {
        Span<char> buffer = stackalloc char[48];
        int len = _deriver.DeriveAddress(entropy16, buffer);
        ReadOnlySpan<char> address = buffer[..len];

        if (_patterns.TryMatch(address, out MatchPosition position, out string pattern))
        {
            match = new Match(new string(address), Seed.Encode(entropy16), pattern, position);
            return true;
        }

        match = default;
        return false;
    }
}
