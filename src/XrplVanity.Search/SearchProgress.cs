using System;
using System.Collections.Generic;

namespace XrplVanity.Search;

public enum TargetState { Pending, Opening, Running, Finished, Failed }

/// <summary>
/// One target's live figures. <paramref name="Seconds"/> and <paramref name="RatePerSecond"/> are
/// target-local, not session-wide: a device's clock starts when that device finished opening (its
/// context creation and OpenCL program compile fall outside it), the CPU pool's is the pool's own
/// elapsed time. <paramref name="ConsistencyErrors"/> counts device hits the CPU reference could
/// not reproduce; it is always 0 for the CPU target. The two trailing parameters have defaults so
/// that existing positional constructions keep compiling.
/// </summary>
public sealed record TargetProgress(
    string Id,
    string Name,
    TargetState State,
    long Candidates,
    double RatePerSecond,
    string? Failure,
    double Seconds = 0,
    long ConsistencyErrors = 0);

public sealed record SearchProgress(
    TimeSpan Elapsed,
    long TotalCandidates,
    double TotalRatePerSecond,
    long Reported,
    IReadOnlyList<TargetProgress> Targets);

public enum SessionOutcome { CountReached, Cancelled, AllTargetsFailed }

public sealed record SearchSummary(SessionOutcome Outcome, SearchProgress Final);
