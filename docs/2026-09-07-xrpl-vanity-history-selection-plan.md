# History Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The History page can select several or all results to export or delete together, and every destructive action asks for confirmation in a dialog.

**Architecture:** Selection lives in `ResultsList` behind an opt-in parameter, keyed by address so the filter and store reloads cannot corrupt it. A single `ConfirmDialog` component serves all three destructive paths; `HistoryPage` owns which one is pending. Bulk deletion goes through a new `IResultStore.DeleteMany` so the encrypted file is rewritten once.

**Tech Stack:** .NET 10, Blazor components in `XrplVanity.App.Core`, bUnit + xUnit v3 tests.

**Spec:** `docs/2026-09-07-xrpl-vanity-history-selection-design.md` (visual reference: `docs/mockups/xrpl-vanity-history-selection.html`)

## Global Constraints

- Tests are xUnit v3 executables. NEVER run `dotnet test`. Run the app suite with
  `dotnet run --project tests/XrplVanity.App.Tests -c Release` and one class with
  `-- -class "*ResultsListTests"`.
- Both suites must stay green: `XrplVanity.App.Tests` was 128 and `XrplVanity.Tests` 282 at the
  branch point, 0 failed and 0 skipped in each.
- Do NOT launch the desktop application; the user checks the UI himself.
- C# style: explicit types, never `var`; `_camelCase` private fields; comments only where they
  explain a decision, in English.
- Seeds appear in exactly two places and nowhere else: `results.dat` and a file the user chose in
  the export save dialog. The confirmation dialog shows addresses only — never a seed.
- The layout must keep working below 1000 px, where the results row drops its target and time
  columns.
- Commits: conventional format, English, and no attribution trailers of any kind.
- Never run `git stash`; the stash stack is shared with other checkouts.

---

### Task 1: `DeleteMany` on the result store

**Files:**
- Modify: `src/XrplVanity.App.Core/Services/IResultStore.cs`
- Modify: `src/XrplVanity.App.Core/Storage/DpapiResultStore.cs`
- Modify: `tests/XrplVanity.App.Tests/Fakes.cs` (`MemoryResultStore`)
- Test: `tests/XrplVanity.App.Tests/DpapiResultStoreTests.cs`

**Interfaces:**
- Produces: `void IResultStore.DeleteMany(IEnumerable<string> addresses)` — removes every named
  address in one write; unknown addresses are ignored; an empty sequence writes nothing.
  `MemoryResultStore.DeleteManyCalls` counts invocations so a later task can prove the bulk path
  is the one taken.

- [ ] **Step 1: Write the failing tests**

Add to `DpapiResultStoreTests`, following the fixture pattern the existing tests in that file use
for a temporary folder:

```csharp
    [Fact]
    public void DeleteMany_Removes_Every_Named_Address_And_Keeps_The_Rest()
    {
        using TempFolder folder = new();
        DpapiResultStore store = new(folder.Path);
        store.Append(new StoredResult("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new StoredResult("rB", "s2", "b", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new StoredResult("rC", "s3", "c", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.DeleteMany(new[] { "rA", "rC" });

        Assert.Equal(new[] { "rB" }, store.LoadAll().Select(r => r.Address).ToArray());
        Assert.Equal(new[] { "rB" }, new DpapiResultStore(folder.Path).LoadAll().Select(r => r.Address).ToArray());
    }

    [Fact]
    public void DeleteMany_Ignores_An_Address_That_Is_Not_Stored()
    {
        using TempFolder folder = new();
        DpapiResultStore store = new(folder.Path);
        store.Append(new StoredResult("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.DeleteMany(new[] { "rZ" });

        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void DeleteMany_With_Nothing_To_Delete_Leaves_The_Store_Alone()
    {
        using TempFolder folder = new();
        DpapiResultStore store = new(folder.Path);
        store.Append(new StoredResult("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.DeleteMany(Array.Empty<string>());

        Assert.Single(store.LoadAll());
    }
```

If the existing file has no `TempFolder` helper, use whatever it already does to get a scratch
folder — do not introduce a second mechanism.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*DpapiResultStoreTests"`
Expected: a compile error on `DeleteMany`.

- [ ] **Step 3: Implement**

In `IResultStore`, beside `Delete`:

```csharp
    /// <summary>
    /// Removes every named address in one rewrite. The store re-encrypts and rewrites the whole
    /// file on each change, so deleting a selection through <see cref="Delete"/> would leave one
    /// crash window per row; this leaves one for the batch.
    /// </summary>
    void DeleteMany(IEnumerable<string> addresses);
```

In `DpapiResultStore`, beside `Delete`:

```csharp
    public void DeleteMany(IEnumerable<string> addresses)
    {
        HashSet<string> doomed = new(addresses, StringComparer.Ordinal);
        lock (_gate)
        {
            List<StoredResult> current = EnsureLoaded();
            ThrowIfReadOnly();
            if (doomed.Count == 0)
            {
                return;
            }

            List<StoredResult> updated = current.Where(r => !doomed.Contains(r.Address)).ToList();
            Write(updated);
            _cache = updated;
        }
    }
```

The read-only check runs before the empty-sequence early return so a read-only store reports
itself the same way for every write method.

In `MemoryResultStore` (test fake), mirror it and count the calls:

```csharp
    private int _deleteManyCalls;

    /// <summary>Bulk deletes: the History page must take this path, not a loop over Delete.</summary>
    public int DeleteManyCalls => Volatile.Read(ref _deleteManyCalls);

    public void DeleteMany(IEnumerable<string> addresses)
    {
        Interlocked.Increment(ref _deleteManyCalls);
        HashSet<string> doomed = new(addresses, StringComparer.Ordinal);
        ThrowIfBroken();
        lock (_items) _items.RemoveAll(r => doomed.Contains(r.Address));
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*DpapiResultStoreTests"`
Then the whole app suite: `dotnet run --project tests/XrplVanity.App.Tests -c Release`

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): the result store can delete a batch of addresses in one write"
```

---

### Task 2: the confirmation dialog

**Files:**
- Create: `src/XrplVanity.App.Core/Components/ConfirmDialog.razor`, `.razor.cs`, `.razor.css`
- Test: `tests/XrplVanity.App.Tests/ConfirmDialogTests.cs`

**Interfaces:**
- Produces: `ConfirmDialog` with parameters `Visible` (bool), `Title` (string), `Message`
  (string), `Addresses` (`IReadOnlyList<string>?`, null for none), `ConfirmLabel` (string),
  `OnConfirm` (`EventCallback`), `OnCancel` (`EventCallback`). Renders `.modal-box` only when
  `Visible`; at most `MaxListedAddresses` = 10 addresses, then `…and N more`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using Xunit;

namespace XrplVanity.App.Tests;

public class ConfirmDialogTests : BunitContext
{
    [Fact]
    public void Hidden_Dialog_Renders_Nothing()
    {
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p.Add(x => x.Visible, false));
        Assert.Empty(cut.FindAll(".modal-box"));
    }

    [Fact]
    public void Shows_The_Title_Message_And_Confirm_Label()
    {
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Title, "Delete 3 results?")
            .Add(x => x.Message, "Their seeds are deleted with them.")
            .Add(x => x.ConfirmLabel, "Delete 3"));

        Assert.Contains("Delete 3 results?", cut.Find(".modal-box h3").TextContent);
        Assert.Contains("Their seeds are deleted with them.", cut.Find(".modal-box p").TextContent);
        Assert.Equal("Delete 3", cut.Find("button.confirm").TextContent.Trim());
    }

    [Fact]
    public void Lists_The_Addresses_And_Truncates_Past_Ten()
    {
        string[] addresses = Enumerable.Range(0, 24).Select(i => $"rAddress{i}").ToArray();
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Addresses, addresses));

        string listed = cut.Find(".what").TextContent;
        Assert.Contains("rAddress0", listed);
        Assert.Contains("rAddress9", listed);
        Assert.DoesNotContain("rAddress10", listed);
        Assert.Contains("and 14 more", listed);
    }

    [Fact]
    public void Without_Addresses_There_Is_No_List()
    {
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p.Add(x => x.Visible, true));
        Assert.Empty(cut.FindAll(".what"));
    }

    [Fact]
    public void Confirm_And_Cancel_Raise_Their_Callbacks()
    {
        int confirms = 0;
        int cancels = 0;
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnConfirm, () => confirms++)
            .Add(x => x.OnCancel, () => cancels++));

        cut.Find("button.confirm").Click();
        cut.Find("button.cancel").Click();

        Assert.Equal(1, confirms);
        Assert.Equal(1, cancels);
    }

    [Fact]
    public void Escape_Cancels_And_Other_Keys_Do_Not()
    {
        int cancels = 0;
        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnCancel, () => cancels++));

        cut.Find(".modal-box").KeyDown("a");
        Assert.Equal(0, cancels);

        cut.Find(".modal-box").KeyDown("Escape");
        Assert.Equal(1, cancels);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*ConfirmDialogTests"`

- [ ] **Step 3: Implement**

`ConfirmDialog.razor` — modelled on `ClosePrompt.razor`, which is the existing in-page modal:

```razor
@if (Visible)
{
  <div class="modal-backdrop"></div>
  <div class="modal-box card-box" tabindex="-1" @onkeydown="OnKeyDown" @ref="_box">
    <h3>@Title</h3>
    <p>@Message</p>
    @if (Addresses is { Count: > 0 })
    {
      <div class="what mono">
        @foreach (string address in Addresses.Take(MaxListedAddresses))
        {
          <div>@address</div>
        }
        @if (Addresses.Count > MaxListedAddresses)
        {
          <div class="more">…and @(Addresses.Count - MaxListedAddresses) more</div>
        }
      </div>
    }
    <div class="buttons">
      <button class="btn-danger confirm t" @onclick="() => OnConfirm.InvokeAsync()">@ConfirmLabel</button>
      <button class="btn-secondary-dark cancel t" @ref="_cancel" @onclick="() => OnCancel.InvokeAsync()">Cancel</button>
    </div>
  </div>
}
```

`ConfirmDialog.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace XrplVanity.App.Core.Components;

public partial class ConfirmDialog
{
    /// <summary>Longer selections are summarised: the dialog is a check, not a manifest.</summary>
    public const int MaxListedAddresses = 10;

    private ElementReference _box;
    private ElementReference _cancel;
    private bool _wasVisible;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public IReadOnlyList<string>? Addresses { get; set; }
    [Parameter] public string ConfirmLabel { get; set; } = "Delete";
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnCancel.InvokeAsync() : Task.CompletedTask;

    /// <summary>
    /// Focus lands on Cancel, not on the destructive button: a dialog that opens under a finger
    /// already pressing Enter must not delete anything. The box itself is focusable so Escape
    /// reaches the handler even before the user has touched a control.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Visible && !_wasVisible)
        {
            _wasVisible = true;
            try
            {
                await _cancel.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // The element is gone (the dialog closed in the same batch); nothing to focus.
            }
        }
        else if (!Visible)
        {
            _wasVisible = false;
        }
    }
}
```

If `FocusAsync` proves unusable under bUnit's renderer, keep the call and guard it — do not drop
the focus behaviour, and say so in the report.

`ConfirmDialog.razor.css` — copy the backdrop and box rules from `ClosePrompt.razor.css` and add:

```css
.what { background: var(--bg); border: 1px solid var(--dark-gray); border-radius: var(--radius-chip); padding: 8px 10px; margin: 10px 0 4px; font-size: 12px; max-height: 180px; overflow-y: auto; }
.what .more { color: var(--text-secondary); }
```

If a `.btn-danger` rule does not already exist in the application's global stylesheet, add one
there in the style of `.btn-primary-mint`, using `--negative` as the background and the dark page
colour as the text — the mockup's `.btn-danger` is the reference.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*ConfirmDialogTests"`

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): a confirmation dialog for destructive history actions"
```

---

### Task 3: selection in the results list

**Files:**
- Modify: `src/XrplVanity.App.Core/Components/ResultsList.razor`, `.razor.cs`, `.razor.css`
- Test: `tests/XrplVanity.App.Tests/ResultsListTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ResultsList` parameters `Selectable` (bool, default false) and `OnDeleteSelected`
  (`EventCallback<IReadOnlyList<SessionResult>>`). With `Selectable` off the component renders
  exactly as before.

- [ ] **Step 1: Write the failing tests**

Add to `ResultsListTests`, following the existing `Items(...)` / render helpers in that file:

```csharp
    [Fact]
    public void Without_Selectable_There_Are_No_Checkboxes()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults());
        Assert.Empty(cut.FindAll("input.pick"));
        Assert.Empty(cut.FindAll("input.pick-all"));
        Assert.Empty(cut.FindAll(".sel-bar"));
    }

    [Fact]
    public void Picking_A_Row_Shows_The_Bar_With_The_Count()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);

        Assert.Empty(cut.FindAll(".sel-bar"));
        cut.FindAll("input.pick")[0].Change(true);

        Assert.Contains("1 selected", cut.Find(".sel-bar").TextContent);
        Assert.True(cut.FindAll(".result-row")[0].ClassList.Contains("picked"));
    }

    [Fact]
    public void Pick_All_Selects_Every_Visible_Row_And_Clears_Them_Again()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);

        cut.Find("input.pick-all").Change(true);
        Assert.Contains("3 selected", cut.Find(".sel-bar").TextContent);

        cut.Find("input.pick-all").Change(false);
        Assert.Empty(cut.FindAll(".sel-bar"));
    }

    [Fact]
    public void Clear_Selection_Empties_The_Selection()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);
        cut.Find("input.pick-all").Change(true);

        cut.Find("button.clear-selection").Click();

        Assert.Empty(cut.FindAll(".sel-bar"));
    }

    [Fact]
    public void A_Selected_Row_That_Leaves_The_List_Is_Dropped_From_The_Selection()
    {
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(all, selectable: true);
        cut.Find("input.pick-all").Change(true);

        cut.SetParametersAndRender(p => p.Add(x => x.Items, new[] { all[0] }));

        Assert.Contains("1 selected", cut.Find(".sel-bar").TextContent);
    }

    [Fact]
    public void Export_Selected_Writes_Only_The_Selected_Rows()
    {
        // …arrange the fake file dialog with a temp path the way Export_Writes_Rows_Chronologically_Oldest_First does…
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);
        cut.FindAll("input.pick")[0].Change(true);

        cut.Find("button.export-selected").Click();

        string[] lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.StartsWith("rOne\t", lines[0]);
    }

    [Fact]
    public void The_Header_Export_Is_Disabled_While_A_Selection_Exists()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);
        Assert.False(cut.Find("button.export").HasAttribute("disabled"));

        cut.FindAll("input.pick")[0].Change(true);

        Assert.True(cut.Find("button.export").HasAttribute("disabled"));
    }

    [Fact]
    public void Delete_Selected_Hands_Back_Exactly_The_Selected_Items()
    {
        IReadOnlyList<SessionResult>? received = null;
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(all, selectable: true, onDeleteSelected: v => received = v);
        cut.FindAll("input.pick")[0].Change(true);
        cut.FindAll("input.pick")[2].Change(true);

        cut.Find("button.delete-selected").Click();

        Assert.Equal(new[] { all[0].Address, all[2].Address }, received!.Select(r => r.Address).ToArray());
    }

    [Fact]
    public void Without_A_Delete_Selected_Callback_The_Bar_Offers_Only_Export()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);
        cut.FindAll("input.pick")[0].Change(true);

        Assert.Empty(cut.FindAll("button.delete-selected"));
        Assert.Single(cut.FindAll("button.export-selected"));
    }
```

Add `ThreeResults()` returning three `SessionResult` values with addresses `rOne`, `rTwo`,
`rThree` and ascending `FoundAt`, and a `RenderList(items, selectable, onDeleteSelected)` helper,
unless equivalents already exist in the file — reuse them if they do.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*ResultsListTests"`

- [ ] **Step 3: Implement**

`ResultsList.razor.cs`:

```csharp
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    [Parameter] public bool Selectable { get; set; }
    [Parameter] public EventCallback<IReadOnlyList<SessionResult>> OnDeleteSelected { get; set; }
```

In `OnParametersSet`, beside the existing pruning of `_revealed` and `_animated` against
`present`, add `_selected.IntersectWith(present);` — a row deleted elsewhere, or hidden by a
filter, leaves the selection by the same rule as the rest of the per-address state.

```csharp
    private bool AllPicked => _items.Count > 0 && _items.All(i => _selected.Contains(i.Address));

    private void Pick(string address, bool picked)
    {
        if (picked) _selected.Add(address); else _selected.Remove(address);
    }

    private void PickAll(bool picked)
    {
        _selected.Clear();
        if (!picked) return;
        foreach (SessionResult item in _items) _selected.Add(item.Address);
    }

    private List<SessionResult> SelectedItems() => _items.Where(i => _selected.Contains(i.Address)).ToList();
```

Rework `Export` to take the rows it writes so the two buttons share one implementation and one
error path:

```csharp
    private async Task Export(IReadOnlyList<SessionResult> rows)
    {
        _exportError = null;
        try
        {
            string? path = await Dialog.PickSaveTsvAsync("xrpl-vanity.tsv");
            if (path is null) return;

            // Items is newest-first in the app; export chronologically (oldest-first), matching
            // the order the CLI's --out would have written.
            TsvExport.Write(path, rows.OrderBy(i => i.FoundAt).Select(i => (i.Address, i.Seed)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _exportError = $"Export failed: {ex.Message}";
        }
    }
```

`ResultsList.razor` — the header export becomes
`disabled="@(Items.Count == 0 || _selected.Count > 0)"` and calls `Export(_items)`; a selection
bar goes directly under the head:

```razor
  @if (Selectable && _selected.Count > 0)
  {
    <div class="sel-bar">
      <b>@_selected.Count selected</b>
      <span class="acts">
        <button class="btn-secondary-dark export-selected t" @onclick="() => Export(SelectedItems())">Export selected…</button>
        @if (OnDeleteSelected.HasDelegate)
        {
          <button class="btn-secondary-dark danger delete-selected t" @onclick="() => OnDeleteSelected.InvokeAsync(SelectedItems())">Delete selected</button>
        }
        <button class="btn-secondary-dark clear-selection t" @onclick="() => PickAll(false)">Clear selection</button>
      </span>
    </div>
  }
```

The "select all" checkbox belongs beside the list's own head — `HistoryPage` owns the filter row,
and this component must not reach into it:

```razor
  @if (Selectable && Items.Count > 0)
  {
    <label class="pick-all-label"><input class="pick-all" type="checkbox" checked="@AllPicked" @onchange="e => PickAll((bool)e.Value!)" /> select all shown</label>
  }
```

Each row gains, as its first cell:

```razor
@if (Selectable) { <input class="pick" type="checkbox" checked="@_selected.Contains(item.Address)" @onchange="e => Pick(item.Address, (bool)e.Value!)" /> }
```

and the row's class list gains `@(Selectable ? "has-select" : "")` and
`@(_selected.Contains(item.Address) ? "picked" : "")`.

`ResultsList.razor.css` — the row already has four column templates and a media query. Adding a
selection column must not double them, so move the template into a custom property and prefix it
once:

```css
.result-row { --cols: minmax(280px, 1.2fr) 28px 1fr auto minmax(120px, 0.8fr) 28px 28px; display: grid; grid-template-columns: var(--cols); gap: 8px; align-items: center; height: 44px; border-top: 1px solid var(--dark-gray); }
.result-row.has-target { --cols: minmax(280px, 1.2fr) 28px 1fr auto auto minmax(120px, 0.8fr) 28px 28px; }
.result-row.has-delete { --cols: minmax(280px, 1.2fr) 28px 1fr auto minmax(120px, 0.8fr) 28px 28px 28px; }
.result-row.has-target.has-delete { --cols: minmax(280px, 1.2fr) 28px 1fr auto auto minmax(120px, 0.8fr) 28px 28px 28px; }
/* One rule covers every combination above: the checkbox is a column in front of whichever
   template applies, so the four variants do not become eight. */
.result-row.has-select { grid-template-columns: 24px var(--cols); }
.result-row.picked { background: rgba(100, 207, 149, 0.07); }
.sel-bar { display: grid; grid-template-columns: auto 1fr; gap: 10px; align-items: center; margin-top: 10px; padding: 7px 10px; border-radius: var(--radius-chip); background: rgba(100, 207, 149, 0.10); font-size: 13px; }
.sel-bar b { color: var(--mint); font-weight: 600; }
.sel-bar .acts { display: flex; gap: 8px; justify-content: flex-end; }
.sel-bar .danger { color: var(--negative); }
.pick-all-label { display: flex; align-items: center; gap: 8px; color: var(--text-secondary); font-size: 12px; margin-top: 8px; }
```

and inside the existing `@media (max-width: 1000px)` block, replace the two
`grid-template-columns` declarations with `--cols` declarations of the same values, so
`.has-select` keeps working at narrow widths.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*ResultsListTests"`
Then the whole app suite. The Search page's tests must pass untouched — that is the proof the
opt-in default changed nothing.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): the results list can select rows to export or delete together"
```

---

### Task 4: the History page asks before it destroys

**Files:**
- Modify: `src/XrplVanity.App.Core/Pages/HistoryPage.razor`, `.razor.cs`
- Test: `tests/XrplVanity.App.Tests/HistoryPageTests.cs`

**Interfaces:**
- Consumes: `ConfirmDialog` (Task 2), `ResultsList.Selectable`/`OnDeleteSelected` (Task 3),
  `IResultStore.DeleteMany` (Task 1).

- [ ] **Step 1: Write the failing tests**

Rewrite the two existing tests that depend on the two-step clear button
(`Clear_Confirmation_Reverts_To_Clear_History_After_The_Confirm_Window_Elapses` goes away
entirely — the mechanism it pins no longer exists) and add:

```csharp
    [Fact]
    public void Deleting_A_Row_Asks_First_And_Cancelling_Keeps_It()
    {
        // …arrange two stored results as the existing tests do…
        cut.FindAll("button.delete")[0].Click();

        Assert.Contains("Delete this result?", cut.Find(".modal-box h3").TextContent);
        Assert.Equal(2, store.LoadAll().Count);

        cut.Find("button.cancel").Click();

        Assert.Empty(cut.FindAll(".modal-box"));
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void Confirming_A_Row_Delete_Removes_Exactly_That_Row()
    {
        cut.FindAll("button.delete")[0].Click();
        cut.Find("button.confirm").Click();

        StoredResult remaining = Assert.Single(store.LoadAll());
        Assert.Equal("rSlow2", remaining.Address);
    }

    [Fact]
    public void Deleting_A_Selection_Goes_Through_The_Bulk_Write()
    {
        cut.Find("input.pick-all").Change(true);
        cut.Find("button.delete-selected").Click();

        Assert.Contains("Delete 2 results?", cut.Find(".modal-box h3").TextContent);
        cut.Find("button.confirm").Click();

        Assert.Empty(store.LoadAll());
        Assert.Equal(1, store.DeleteManyCalls);
    }

    [Fact]
    public void Clear_History_Asks_Once_Instead_Of_Arming()
    {
        cut.Find("button.clear").Click();

        Assert.Contains("Clear the whole history?", cut.Find(".modal-box h3").TextContent);
        Assert.Equal(2, store.LoadAll().Count);
        Assert.Equal("Clear history", cut.Find("button.clear").TextContent.Trim());

        cut.Find("button.confirm").Click();
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void A_Failing_Bulk_Delete_Shows_The_Reason_Instead_Of_Crashing()
    {
        // store.ThrowOnWrite = true after seeding, as the existing banner test does
        cut.Find("input.pick-all").Change(true);
        cut.Find("button.delete-selected").Click();
        cut.Find("button.confirm").Click();

        Assert.Contains("read-only", cut.Find(".banner").TextContent, StringComparison.OrdinalIgnoreCase);
    }
```

Keep `A_Read_Only_Store_Shows_The_Reason_And_Hides_Destructive_Buttons` and extend it: the
checkbox column is still rendered and `button.delete-selected` never appears.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*HistoryPageTests"`

- [ ] **Step 3: Implement**

Delete `ClearConfirmWindow`, `_clearArmedAt`, `_clearRevertTimer`, `ArmClearRevertTimer` and
`StopClearRevertTimer`; the page no longer needs `TimeProvider` for that (leave the injection if
anything else uses it, remove it if nothing does). `Dispose` keeps whatever remains.

Model the pending action as one small private type so the dialog has a single source of truth:

```csharp
    /// <summary>What a confirmed dialog will do. Null while no dialog is open.</summary>
    private sealed record PendingAction(string Title, string Message, string ConfirmLabel,
        IReadOnlyList<string>? Addresses, Action Execute);

    private PendingAction? _pending;
```

The three openers:

```csharp
    private void AskDelete(SessionResult item)
        => _pending = new PendingAction(
            "Delete this result?",
            "Its seed is deleted with it. results.dat is the only place the seed exists, so this cannot be undone.",
            "Delete",
            new[] { item.Address },
            () => Write(() => Store.Delete(item.Address)));

    private void AskDeleteSelected(IReadOnlyList<SessionResult> items)
    {
        if (items.Count == 0) return;
        string[] addresses = items.Select(i => i.Address).ToArray();
        _pending = new PendingAction(
            $"Delete {addresses.Length} result{(addresses.Length == 1 ? string.Empty : "s")}?",
            "Their seeds are deleted with them. results.dat is the only place those seeds exist, so this cannot be undone.",
            $"Delete {addresses.Length}",
            addresses,
            () => Write(() => Store.DeleteMany(addresses)));
    }

    private void AskClear()
        => _pending = new PendingAction(
            "Clear the whole history?",
            $"All {_all.Count} results and their seeds are removed from results.dat. This cannot be undone, and the filter does not narrow it — everything goes.",
            "Clear history",
            null,
            () => Write(Store.Clear));

    private void Confirm()
    {
        PendingAction? pending = _pending;
        _pending = null;
        pending?.Execute();
    }

    // The store turns read-only mid-process when DPAPI cannot write results.dat, and then throws
    // on every write. A Blazor event handler is the worst place for that to escape, so every
    // destructive action reports the reason as a banner and re-reads whatever is still on disk.
    private void Write(Action action)
    {
        _storeError = null;
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            _storeError = ex.Message;
        }

        Reload();
    }
```

`HistoryPage.razor`: the clear button becomes
`<button class="btn-secondary-dark clear t" @onclick="AskClear">Clear history</button>`, the list
becomes

```razor
  <ResultsList Items="Filtered()" ShowTarget="true" Selectable="true" OnDelete="AskDelete"
               OnDeleteSelected="@(Store.IsReadOnly ? default(EventCallback<IReadOnlyList<SessionResult>>) : EventCallback.Factory.Create<IReadOnlyList<SessionResult>>(this, AskDeleteSelected))"
               RevealSeconds="Vm.Settings.SeedRevealSeconds" ClipboardClearSeconds="Vm.Settings.ClipboardClearSeconds" />
```

— a read-only store gets no bulk-delete callback, which is what makes the button absent rather
than disabled. Apply the same rule to `OnDelete`, matching how the header's destructive buttons
already disappear. If that expression reads badly inline, compute both callbacks in the
code-behind and bind the fields; the requirement is the behaviour, not the spelling.

The dialog goes at the end of the page:

```razor
<ConfirmDialog Visible="@(_pending is not null)" Title="@(_pending?.Title ?? string.Empty)"
               Message="@(_pending?.Message ?? string.Empty)" Addresses="_pending?.Addresses"
               ConfirmLabel="@(_pending?.ConfirmLabel ?? "Delete")"
               OnConfirm="Confirm" OnCancel="() => _pending = null" />
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*HistoryPageTests"`
Then both suites in full.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): history confirms every delete and can act on a selection"
```

---

### Task 5: documentation

**Files:**
- Modify: `docs/desktop-app.md`

- [ ] **Step 1: Write the documentation**

In the History part of the page description, replace the sentence about per-row delete and the
two-step "Clear history" with the new behaviour: a checkbox column, a "select all shown" box, the
selection bar with its three buttons, and one confirmation dialog for the row delete, the bulk
delete and the clear. State that the dialog names the seed as the thing that cannot be recovered,
that Cancel holds focus and Escape closes it, and that a read-only store keeps export while both
delete paths disappear.

Add a checklist entry where the other manual-check lines are, numbered in sequence with them:
select two rows, export them and confirm the file holds exactly those two lines, then delete them
and confirm the dialog names both addresses.

Do not touch `README.md`: the option table and the CLI are unaffected.

- [ ] **Step 2: Validate and commit**

```bash
PYTHONIOENCODING=utf-8 python "C:/Users/Evril/.claude/skills/developer-documentation/scripts/validate_docs.py" docs/desktop-app.md
git add docs
git commit -m "docs: history selection, bulk actions and the confirmation dialog"
```

---

## Self-review

- **Spec coverage.** `DeleteMany` → Task 1; the dialog → Task 2; the checkbox column, the bar and
  subset export → Task 3; the three destructive paths, the removal of the arming button and the
  read-only rule → Task 4; documentation → Task 5. The "not built" list (range selection, undo,
  persisted selection) is absent from every task, as intended.
- **Placeholders.** Two tests in Task 3 and four in Task 4 elide the arrangement with a comment
  pointing at the existing test that does the same setup, because that setup is long and already
  written in those files; every assertion is spelled out. Everything else carries its code.
- **Type consistency.** `IReadOnlyList<SessionResult>` is what `OnDeleteSelected` carries in both
  Task 3 and Task 4; `IEnumerable<string>` is what `DeleteMany` takes in Tasks 1 and 4;
  `ConfirmDialog`'s parameter names in Task 2 are the ones Task 4 binds.
