# History: multi-select, bulk export and delete confirmation

Design for two changes to the desktop application's History page: destructive actions ask for
confirmation in a dialog, and rows can be selected — several, or all of them — to export or
delete together.

Visual reference: `docs/mockups/xrpl-vanity-history-selection.html`.

## Why

Every stored result carries a seed, and `results.dat` is the only place that seed exists. Today
the per-row delete button acts on the first click: one mis-aimed click on a 40-pixel row destroys
a key permanently. "Clear history" is safer — it arms for five seconds and needs a second click —
but the two mechanisms disagree, and the arming button is easy to confirm by reflex.

Bulk work is the second gap. A user who ran a long search and wants to keep three addresses out
of forty deletes thirty-seven rows one click at a time, and exporting a subset is impossible:
`Export TSV…` writes every row the filter shows.

## What is built

### Selection

The History page's list gains a leading checkbox column. A second checkbox sits in the filter
row and selects or clears every row the filter currently shows; it renders as a dash when only
part of the visible list is selected.

Selection is held as a set of addresses, not of row indices, so:

- typing in the filter hides rows without unselecting them, and the count keeps telling the
  truth about what is held;
- reloading the store after a delete drops the addresses that no longer exist;
- the "select all" checkbox reflects and acts on the **visible** rows only, which is what the
  filter above it leads a reader to expect.

The Search page's results list is unchanged. Selection lives on History, where deleting is
possible; `ResultsList` gains the behaviour behind an opt-in parameter and renders exactly as
today when it is off.

### The selection bar

When at least one row is selected, a bar appears under the filter row:

```
3 selected            [Export selected…] [Delete selected] [Clear selection]
```

While a selection exists, the header's `Export TSV…` is disabled: the bar is where exporting
happens, and one button must not silently change which rows it writes. `Clear history` in the
header keeps its meaning — everything, regardless of filter or selection.

Selected rows are tinted so the selection is legible without reading the checkboxes.

### The confirmation dialog

One component covers all three destructive paths, rendered inside the page like the existing
close prompt rather than as a system message box:

| Path | Title | Confirm button |
| --- | --- | --- |
| Row delete button | `Delete this result?` | `Delete` |
| `Delete selected` | `Delete 3 results?` | `Delete 3` |
| `Clear history` | `Clear the whole history?` | `Clear history` |

The body always names what is lost — the seed, not the row: *"Its seed is deleted with it.
`results.dat` is the only place the seed exists, so this cannot be undone."* The row and
selection variants list the addresses, truncated after ten with `…and N more`. The clear variant
states the total and says the filter does not narrow it.

The confirming button is red and carries the count. `Cancel` takes focus, so a stray Enter is
harmless, and Esc cancels. The two-step `Clear history` button and its five-second revert timer
are removed: with a dialog on every path, arming is a second, inconsistent mechanism for the
same job.

### Bulk store writes

`IResultStore` gains `DeleteMany(IEnumerable<string> addresses)`. `DpapiResultStore` rewrites the
whole encrypted file on every change, so deleting thirty rows through `Delete` would encrypt and
rewrite thirty times, each write a window in which a crash truncates the history. `DeleteMany`
is one rewrite, like `AppendRange` beside it. `Delete` stays for the single-row path.

### Export of a subset

`TsvExport.Write` already takes the rows to write. The list component learns to export either
everything it holds or only the selected rows, in both cases oldest-first, matching what the
CLI's `--out` would have written. Nothing about the file format changes.

## Behaviour at the edges

- **Read-only store.** When `results.dat` cannot be written, the red banner already replaces the
  storage footer and the destructive buttons disappear. The checkbox column and
  `Export selected…` stay — reading and exporting remain safe — while `Delete selected` and
  `Clear history` are absent rather than disabled, since nothing the user can do would make them
  work.
- **A write failing mid-action.** The store turns read-only when DPAPI fails, and then throws on
  every write. Both bulk paths keep today's guard: catch `InvalidOperationException`, show the
  reason as a banner, and re-read whatever is still on disk.
- **A search running behind the page.** History re-reads the store when the session's result
  count changes. A new hit arriving while rows are selected must not disturb the selection; it
  is a new address, and the selection is a set of addresses.
- **Deleting everything that is selected** leaves the selection empty and the bar gone, with no
  separate cleanup step.
- **An empty selection** cannot reach a bulk action: the bar is not rendered.

## What is deliberately not built

- Range selection with Shift-click. The list is virtualised and the filter is the tool for
  narrowing; a range gesture over a virtualised list is a larger piece of work than the request.
- Undo. There is nowhere to keep a deleted seed that would not be a second copy of the secret,
  which is the thing the storage design exists to prevent. The dialog is the safety net.
- Selection persisted across restarts. It is a working set, not a setting.

## Security

Nothing changes about where seeds live. The dialog lists addresses, never seeds. The export path
is the one that writes seeds to a plain file, and it is unchanged: the same explicit save dialog,
the same format, now optionally over a subset.

## Testing

- `DpapiResultStoreTests`: `DeleteMany` removes exactly the named addresses in one write, ignores
  an address that is not present, is a no-op for an empty sequence, and throws on a read-only
  store.
- `ResultsListTests`: the checkbox column appears only when selection is enabled; the header
  checkbox selects and clears the visible rows and shows the partial state; the bar appears with
  a count; export writes only the selected rows; nothing about the Search page's rendering
  changes.
- A new `ConfirmDialogTests`: the three shapes, the truncation past ten addresses, Cancel and Esc
  cancelling, and the confirm callback firing once.
- `HistoryPageTests`: the row delete opens the dialog instead of deleting; confirming deletes;
  cancelling does not; `Delete selected` goes through `DeleteMany`; `Clear history` no longer
  arms and clears only after confirmation; a read-only store hides both destructive paths and
  keeps export.
