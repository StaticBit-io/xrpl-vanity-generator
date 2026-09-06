using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

public partial class PatternEditor : IDisposable
{
    private string _position = "Starts";
    private string _text = string.Empty;
    private bool _ignoreCase = true;

    [Parameter, EditorRequired] public SearchViewModel Vm { get; set; } = null!;

    protected override void OnInitialized() => Vm.Changed += OnChanged;

    private void OnChanged() => InvokeAsync(StateHasChanged);

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Enter") return;
        Vm.AddPattern(_text, Enum.Parse<MatchPosition>(_position), _ignoreCase);
        _text = string.Empty;
    }

    private async Task OnListChosen(InputFileChangeEventArgs e)
    {
        using StreamReader reader = new(e.File.OpenReadStream(maxAllowedSize: 1024 * 1024));
        string content = await reader.ReadToEndAsync();
        Vm.AddPatternsFromList(content.Split('\n'), _ignoreCase);
    }

    public void Dispose() => Vm.Changed -= OnChanged;
}
