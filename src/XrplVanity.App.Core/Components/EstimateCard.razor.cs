using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public partial class EstimateCard : IDisposable
{
    [Parameter, EditorRequired] public SearchViewModel Vm { get; set; } = null!;
    protected override void OnInitialized() => Vm.Changed += OnChanged;
    private void OnChanged() => InvokeAsync(StateHasChanged);
    public void Dispose() => Vm.Changed -= OnChanged;
}
