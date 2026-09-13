using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

public partial class PatternEditor : ViewModelComponent
{
    /// <summary>Above this many chips the set is cut down until the user asks for the rest.</summary>
    private const int CollapseThreshold = 12;

    /// <summary>How much of a cut-down set stays on screen — about two rows at the card's width.</summary>
    private const int CollapsedChips = 10;

    private string? _listError;
    private string? _listNote;
    private bool _expanded;

    /// <summary>
    /// Raised instead of clearing: the confirmation belongs to the page, which is the only place
    /// that can make everything behind the dialog inert.
    /// </summary>
    [Parameter] public EventCallback OnClearRequested { get; set; }

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Enter") return;
        _listNote = null;

        // AddPatternFromEntry refuses a blank entry and a rule the set already holds alike; only
        // the second deserves a word, so what was in the boxes is read before it clears them.
        bool hadEntry = Vm.EntryPosition == MatchPosition.Pair
            ? !string.IsNullOrWhiteSpace(Vm.EntryText) && !string.IsNullOrWhiteSpace(Vm.EntryEndsText)
            : !string.IsNullOrWhiteSpace(Vm.EntryText);

        if (!Vm.AddPatternFromEntry() && hadEntry)
        {
            _listNote = "That pattern is already in the list.";
        }
    }

    private async Task OnListChosen(InputFileChangeEventArgs e)
    {
        _listError = null;
        _listNote = null;
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

        ListLoadResult result = Vm.AddPatternsFromList(content.Split('\n'), Vm.EntryIgnoreCase);
        _listNote = Describe(result);
    }

    /// <summary>What the load skipped, or null when it took every line the file held.</summary>
    private static string? Describe(ListLoadResult result)
    {
        List<string> parts = new(2);
        if (result.Duplicates > 0)
        {
            parts.Add(result.Duplicates == 1
                ? "1 duplicate skipped."
                : $"{result.Duplicates} duplicates skipped.");
        }

        if (result.Rejected > 0)
        {
            parts.Add(result.Rejected == 1
                ? "1 line could not be read and was skipped."
                : $"{result.Rejected} lines could not be read and were skipped.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private async Task OnSaveList()
    {
        _listError = null;
        _listNote = null;
        IReadOnlyList<PatternChip> chips = Vm.Patterns;
        if (chips.Count == 0) return;

        try
        {
            // The picker itself can fail (no owner window yet, a shell extension refusing the
            // dialog), so it belongs inside the same guard as the write.
            string? path = await Dialog.PickSaveAsync("xrpl-vanity-patterns.txt", "Pattern list", ".txt");
            if (path is null) return;

            PatternListFile.Write(path, chips);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _listError = $"Could not save the list: {ex.Message}";
        }
    }
}
