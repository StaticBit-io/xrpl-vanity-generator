using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Localization;

namespace XrplVanity.App.Core.Components;

public enum SearchState { Idle, Starting, Searching, Stopping }

public partial class StatusPill
{
    [Parameter] public SearchState State { get; set; }

    private TextId Label => State switch
    {
        SearchState.Starting => SearchText.StateStarting,
        SearchState.Searching => SearchText.StateSearching,
        SearchState.Stopping => SearchText.StateStopping,
        _ => SearchText.StateIdle,
    };

    private string Css => State switch
    {
        SearchState.Searching => "pill pill-active breathe",
        SearchState.Starting => "pill pill-active breathe",
        SearchState.Stopping => "pill pill-stopping",
        _ => "pill",
    };
}
