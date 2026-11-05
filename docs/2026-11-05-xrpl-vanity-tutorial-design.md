# XRPL Vanity interactive tutorial: design

Status: draft for the owner's review, 2026-11-05.

## Why

The desktop application shows a lot at once: patterns, an estimate, devices, GPU duty, live
statistics and results share one page. A first-time user can get lost and not understand what they
are doing. The riskiest misunderstandings are a word that would take decades to find, and not
knowing that a seed is the account.

The tutorial teaches by doing, on the real interface, in the real application.

## Goals

After the tutorial, a user can:

1. Find the settings that matter to a newcomer.
2. Build a pattern list, and knows where to save it and load it.
3. Run a search.
4. Read the estimate, and simplify a pattern when the wait is too long.
5. Understand how a result reaches a wallet by QR code. Seeing it is enough; no real scan.
6. Change the GPU load.

The owner defined this list. Goal 5 as "seeing is enough" and "the tutorial search is not saved"
are the owner's decisions.

## Non-goals

- Scanning a QR code or importing into a real wallet.
- A tutorial for the CLI.
- Translations. The application is English only.
- Video, animation for its own sake, scores or badges.

## Anti-goals

The tutorial has failed, even if it works, when:

- A tutorial seed reaches `results.dat`, the history page, a notification or any other file.
- A user's own words, device selection, thread count, stop count or GPU duty are lost or changed
  after the tutorial ends, however it ends, including a crash or a killed process.
- A user cannot get out. No step may be a dead end.
- It nags. It is offered once and never reappears by itself.
- It sends anything over the network or records anything about the user. The application has no
  network code and the privacy policy says so.

## How it looks

The owner chose the spotlight layout from three mockups: the screen is dimmed, only the relevant
part stays lit, and a bubble sits beside it. Colours and fonts come from the application's existing
styles. The bubble shows a title, one or two sentences, the step number and **Skip**.

A step can light several parts at once, and it must on the estimate step: the user edits the
pattern and watches the time react, so both cards stay lit and usable. Everything unlit is inert.

A step moves on by itself when the user has done the action. A step that only explains something
has a **Next** button.

## The steps

1. **Welcome.** On first start, the application offers the tutorial: start, or skip. Skipping says
   where to find it later.
2. **Patterns.** The user adds a word of ten letters or more, which step 4 needs: an address matches
   if any one word does, so a short word added first would hide the long wait. The tutorial then
   points at **save list** and **load list** and moves on with **Next**. Saving a file is optional:
   a system file dialog must not be required, and must not break the tutorial if the user opens one.
3. **Devices and speed.** The user picks devices and presses **measure speed**. Without a measured
   speed a first run has no estimate.
4. **Estimate.** The user sees a wait of years for the long word, then simplifies the pattern until
   the wait is short, and the bubble suggests a word of two or three characters. The step reads
   `SearchViewModel.EstimatedTime`, and **Start** stays unlit until the wait is short.
5. **Search.** The user presses **Start** and gets a real match within seconds. The search stops at
   the first match. An estimate is an average, so an unlucky run can take longer: **Stop** stays
   lit, and after stopping the pattern and the estimate stay lit, so the user can simplify and start
   again.
6. **Result and seed.** The tutorial says that whoever holds the seed controls the account, and
   opens the QR dialog on the tutorial result. The text for this step sits inside the dialog, not in
   a bubble, because a bubble would cover it. No scan is needed. Only the QR button of the result
   is lit: seed reveal, copy and file export stay inert, so the tutorial seed cannot leave the
   application through them.
7. **GPU load.** The user moves **GPU duty**. With no GPU device the step is left out.
8. **Settings.** The tutorial moves to the Settings page and shows **Stop after N matches**,
   **Notifications** and **Data folder**, with **Next** on each. These controls are lit but inert:
   the step explains them, and the user changes them after the tutorial.
9. **Done.** The tutorial result is discarded and the user's state comes back. The last message
   says where to reopen the tutorial.

Settings come last, not first as in the owner's list, so a newcomer sees a result before options.
The owner accepted this order.

## Behaviour

**Offering it.** A new `AppSettings` flag records that the tutorial was offered. Any exit sets it:
finished, skipped or closed. Settings gets a button to run it again.

**A separate run.** The tutorial starts from an empty pattern list, also on a re-run, so a user's own
words cannot slow the search or produce a match that looks like the tutorial's. While the tutorial
is active, `SearchViewModel` must:

- not write matches to `IResultStore`;
- leave `_settings` holding the user's values: `PersistDraft` does nothing, and the `DutyPercent`
  setter changes the live value without calling `SaveSettings`;
- raise no match notifications;
- stop after one match, whatever the user's stop count is.

The tutorial changes only the live fields. `_settings` is never touched, so any write that does
happen, such as `ApplyRateHints` through `SaveSettings`, still saves the user's patterns, targets and
duty. After a crash or a killed process, `settings.json` needs no repair.

On exit it restores the live fields from a snapshot taken at the start: patterns, target selection,
CPU threads, stop count, GPU duty, the entry text, and the session results that `Start` would clear.

**What survives on purpose.** Measured speeds (`RateHints`) are real measurements and are kept.
Nothing else does.

**The tutorial seed** lives in memory only and is dropped on exit. The QR dialog works on it as
usual, including its timers.

**Other ways in.** The tray's start, stop and duty actions are unavailable during the tutorial.
Closing the window leaves the tutorial first, stopping a running tutorial search and restoring the
user's state, and then follows the user's close setting.

**When it cannot run.** The Settings button is disabled while a search or a speed measurement is
running. The tutorial never interrupts a search.

## Constraints

- Blazor Hybrid, in `XrplVanity.App.Core`, with bUnit tests in `XrplVanity.App.Tests`.
- No third-party library and no network access.
- The window defaults to 1100 by 720 and can be resized. A bubble stays fully on screen and does not
  cover what it points at.
- WCAG 2.2 AA: contrast, keyboard use, focus moved to the bubble, Escape offers to leave.
- Interface copy follows the `writing-site-copy` rules. It is English, and has no long dash.
- `docs/desktop-app.md` and the site's `/desktop/` page mention the tutorial. `PRIVACY.md` is checked
  against the new flag in `settings.json`.

## Calls made by the author, not the owner

- **Who is offered it.** Existing users have no flag in `settings.json`. The offer appears only when
  there are no stored results and no patterns, so people already using the application are not
  interrupted. They can start it from Settings. If the result store cannot be read or is read-only,
  the application treats the user as existing and does not offer it.
- **Thresholds.** Step 4 counts as "too long" above one day and "short enough" at ten seconds or less,
  so step 5 finishes within seconds on a typical run.
- **Notifications** are off during the tutorial, so a tutorial address does not appear in the Windows
  notification history.
- **An empty pattern list** at the start of every run, restored on exit.
- **Settings stay inert in step 8**, so the tutorial and the page never edit the same field.
- **Only the QR button of the tutorial result is usable.** Reveal, copy and export stay inert.
- **The tray** cannot start, stop or change the load during the tutorial.

## Left to the builder

- How the spotlight is built. The page has no JavaScript of its own yet; raising the lit components
  above an overlay with a class may need none, and a small interop for positioning is acceptable.
- The structure of the tutorial state and where it lives. It has to survive the move to Settings.
- Step wording, within the rules above.
- What a step does when something fails, such as a failed speed measurement or a search that runs
  much longer than the estimate, as long as it is not a dead end.
