using System;

namespace XrplVanity.Core;

/// <summary>
/// The single place matches pass through: enforces the optional count limit exactly and hands
/// every accepted match to <c>report</c> under one lock, so the callback never runs concurrently
/// and consumers see matches in acceptance order. Shared by every CPU worker and GPU engine.
/// </summary>
public sealed class MatchSink
{
    private readonly object _gate = new();
    private readonly int? _limit;
    private readonly Action<Match> _report;

    public MatchSink(int? limit, Action<Match> report)
    {
        if (limit is int l && l <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The limit must be positive or null.");
        }

        _limit = limit;
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public bool CountSatisfied { get; private set; }
    public long Reported { get; private set; }

    /// <summary>
    /// Reports the match unless the limit is already met. Returns false once the limit is
    /// reached (including on the call that reaches it) so the caller stops searching.
    /// </summary>
    public bool TryReport(Match match)
    {
        lock (_gate)
        {
            if (_limit is int already && Reported >= already)
            {
                return false;
            }

            _report(match);
            Reported++;
            if (_limit is int limit && Reported >= limit)
            {
                CountSatisfied = true;
                return false;
            }

            return true;
        }
    }
}
