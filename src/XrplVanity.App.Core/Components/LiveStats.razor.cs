using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.Search;

namespace XrplVanity.App.Core.Components;

public partial class LiveStats
{
    [Parameter] public SearchProgress? Progress { get; set; }

    private static TextId StateLabel(TargetState state) => state switch
    {
        TargetState.Pending => SearchText.TargetPending,
        TargetState.Opening => SearchText.TargetOpening,
        TargetState.Running => SearchText.TargetRunning,
        TargetState.Finished => SearchText.TargetFinished,
        _ => SearchText.TargetFailed,
    };
}
