using Microsoft.AspNetCore.Components;
using XrplVanity.Search;

namespace XrplVanity.App.Core.Components;

public partial class LiveStats
{
    [Parameter] public SearchProgress? Progress { get; set; }
}
