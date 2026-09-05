using System;
using System.Threading;

namespace XrplVanity.Gpu;

/// <summary>
/// Throttles a device by pausing between launches: after a launch that took <c>elapsed</c>,
/// the caller sleeps <c>elapsed × (100 − duty) / duty</c>, so the device is busy about
/// <c>duty</c> percent of wall time on average and free for other applications otherwise.
/// A launch itself is never interrupted; the granularity is one launch (tens of milliseconds
/// on a GPU).
/// </summary>
public static class DutyCycle
{
    public const int Full = 100;

    /// <summary>The pause that follows a launch of <paramref name="elapsed"/> at <paramref name="dutyPercent"/> (1–100).</summary>
    public static TimeSpan PauseAfter(TimeSpan elapsed, int dutyPercent)
    {
        if (dutyPercent < 1 || dutyPercent > Full)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyPercent), dutyPercent, "The duty cycle must be a whole number from 1 to 100.");
        }

        if (dutyPercent == Full || elapsed <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        double pauseMilliseconds = elapsed.TotalMilliseconds * (Full - dutyPercent) / dutyPercent;
        return TimeSpan.FromMilliseconds(pauseMilliseconds);
    }

    /// <summary>Sleeps for <see cref="PauseAfter"/>; returns early when <paramref name="cancellationToken"/> is cancelled.</summary>
    public static void Pause(TimeSpan elapsed, int dutyPercent, CancellationToken cancellationToken)
    {
        TimeSpan pause = PauseAfter(elapsed, dutyPercent);
        if (pause <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        cancellationToken.WaitHandle.WaitOne(pause);
    }
}
