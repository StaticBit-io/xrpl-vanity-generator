using System;
using System.Collections.Generic;

namespace XrplVanity.Search;

public enum TargetState { Pending, Opening, Running, Finished, Failed }

public sealed record TargetProgress(
    string Id,
    string Name,
    TargetState State,
    long Candidates,
    double RatePerSecond,
    string? Failure);

public sealed record SearchProgress(
    TimeSpan Elapsed,
    long TotalCandidates,
    double TotalRatePerSecond,
    long Reported,
    IReadOnlyList<TargetProgress> Targets);

public enum SessionOutcome { CountReached, Cancelled, AllTargetsFailed }

public sealed record SearchSummary(SessionOutcome Outcome, SearchProgress Final);
