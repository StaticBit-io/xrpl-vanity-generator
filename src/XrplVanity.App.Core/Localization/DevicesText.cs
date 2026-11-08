using System.Runtime.CompilerServices;

namespace XrplVanity.App.Core.Localization;

/// <summary>The Devices card and the duty slider.</summary>
public static class DevicesText
{
    public static readonly TextId Title = Id();
    public static readonly TextId MeasureSpeed = Id();
    public static readonly TextId Measuring = Id();
    public static readonly TextId RefreshDevices = Id();
    public static readonly TextId NoGpuFound = Id();
    public static readonly TextId CpuTag = Id();

    /// <summary>Plural over <c>count</c>; the number itself is drawn beside it.</summary>
    public static readonly TextId Threads = Id();
    public static readonly TextId DutyTitle = Id();

    /// <summary>Value: <c>percent</c>.</summary>
    public static readonly TextId DutyValue = Id();

    /// <summary>Value: <c>percent</c>, the share of time the card stays free.</summary>
    public static readonly TextId DutyNote = Id();

    private static TextId Id([CallerMemberName] string name = "") => TextId.Of("Devices", name);
}
