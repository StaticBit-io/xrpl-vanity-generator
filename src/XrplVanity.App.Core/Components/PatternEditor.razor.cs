using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

public partial class PatternEditor : ViewModelComponent
{
    private string _position = "Starts";
    private string _text = string.Empty;
    private bool _ignoreCase = true;
    private string? _listError;

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Enter") return;
        Vm.AddPattern(_text, Enum.Parse<MatchPosition>(_position), _ignoreCase);
        _text = string.Empty;
    }

    private async Task OnListChosen(InputFileChangeEventArgs e)
    {
        _listError = null;
        string content;
        try
        {
            using StreamReader reader = new(e.File.OpenReadStream(maxAllowedSize: 1024 * 1024));
            content = await reader.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _listError = $"Could not read the list: {ex.Message}";
            return;
        }

        Vm.AddPatternsFromList(content.Split('\n'), _ignoreCase);
    }
}
