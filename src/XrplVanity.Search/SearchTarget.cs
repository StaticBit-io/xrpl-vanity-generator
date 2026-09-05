using System;
using System.Collections.Generic;
using System.Linq;
using XrplVanity.Core;
using XrplVanity.Gpu;

namespace XrplVanity.Search;

/// <summary>Where a session searches: the CPU, an explicit device, or the first usable device.</summary>
public abstract record SearchTarget
{
    private SearchTarget()
    {
    }

    public sealed record Cpu : SearchTarget
    {
        public Cpu(int threads)
        {
            if (threads < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(threads), threads, "At least one CPU thread is required.");
            }

            Threads = threads;
        }

        public int Threads { get; }
    }

    public sealed record Device(DeviceSelector Selector) : SearchTarget;

    /// <summary>The first usable device in backend preference order (the CLI's <c>--gpu</c>).</summary>
    public sealed record FirstAvailableDevice : SearchTarget;
}

public sealed record SearchRequest
{
    public SearchRequest(PatternSet patterns, IReadOnlyList<SearchTarget> targets, int? count, int dutyPercent = DutyCycle.Full)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(targets);
        if (!patterns.HasPatterns)
        {
            throw new ArgumentException("The pattern set has no reachable pattern.", nameof(patterns));
        }

        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one target is required.", nameof(targets));
        }

        if (targets.OfType<SearchTarget.Cpu>().Count() > 1)
        {
            throw new ArgumentException("At most one CPU target is allowed.", nameof(targets));
        }

        if (targets.OfType<SearchTarget.FirstAvailableDevice>().Any() && targets.OfType<SearchTarget.Device>().Any())
        {
            throw new ArgumentException("FirstAvailableDevice cannot be combined with an explicit device.", nameof(targets));
        }

        if (count is int c && c <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "The count must be positive or null.");
        }

        if (dutyPercent < 1 || dutyPercent > DutyCycle.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyPercent), dutyPercent, "The duty cycle must be a whole number from 1 to 100.");
        }

        Patterns = patterns;
        Targets = targets;
        Count = count;
        DutyPercent = dutyPercent;
    }

    public PatternSet Patterns { get; }
    public IReadOnlyList<SearchTarget> Targets { get; }
    public int? Count { get; }
    public int DutyPercent { get; }
}
