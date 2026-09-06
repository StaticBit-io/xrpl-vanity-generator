using Microsoft.AspNetCore.Components;

namespace XrplVanity.App.Core.Components;

public enum SearchState { Idle, Starting, Searching, Stopping }

public partial class StatusPill
{
    [Parameter] public SearchState State { get; set; }

    private string Css => State switch
    {
        SearchState.Searching => "pill pill-active breathe",
        SearchState.Starting => "pill pill-active breathe",
        SearchState.Stopping => "pill pill-stopping",
        _ => "pill",
    };
}
