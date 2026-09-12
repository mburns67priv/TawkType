# Settings → Vocabulary: redesign plan

Written 2026-09-12. Status: **proposed, not built.**

## 0. The tension, resolved up front

The handoff decision ("Vocabulary is a text box, and its own file") is about *bulk*: these lists arrive
pasted, and the user needs to select, sort, diff and keep them in a note. The owner's complaint is
about *first entry*: nobody remembers `=>` or `\n` when adding one rule.

Both are true and they are different moments. So:

- **The structured list is the primary view and the default.** It is what the page opens on, for all
  three lists, in both empty and populated states. Adding and editing go through a modal with real
  fields.
- **Bulk text editing survives as a per-section mode toggle** — a text button labelled *Edit as text*
  on each section header, which swaps that one section's list for the multi-line text box that is
  there today, in place, without leaving the page. A second click (*Done editing text*) returns.
- **The single source of truth is the structured list** (`ObservableCollection` of row view models on
  `SettingsViewModel`). Text mode is a transient projection of it: entering text mode formats from the
  list, leaving text mode parses back into it. This is the only arrangement where Save, Export and the
  modal cannot disagree about what the vocabulary is.

Nothing is lost in the round trip because leaving text mode is *gated*, not lossy — see §3.

## 1. Page layout

Page header is unchanged. Three sections follow in the current order (Spellings, Replacements,
Snippets), then a footer. Every section has the same skeleton, so learning one teaches the other two.

### Section skeleton

```
[Ui.SectionTitle]  Spellings                    3 words        Edit as text
[Ui.Hint]          <one-sentence explanation, wrapped, MaxWidth 520>
[+ Add a spelling]                                  <- Ui.Button, left aligned
[ row ]
[ row ]
```

- **Header row** is a `Grid` with three columns: title (`Ui.SectionTitle`) left; a count right in
  `Ui.Hint` (`"3 words"`, `"1 replacement"`, `"no snippets yet"`); the *Edit as text* toggle far
  right, a borderless button.
- **The hint sits under the title and above the Add button**, with the syntax sentence removed,
  because the syntax is no longer the user's problem:
  - Spellings: *"Names, products and acronyms, written the way you want them typed — TawkType,
    Jupitor Studio, GitHub. However they are heard, they come out like this."*
  - Replacements: *"For the mishearings a spelling cannot fix, where what you say and what you want
    are different words — say "see sharp", get C#."*
  - Snippets: *"Saved text you insert by saying "insert" and the trigger. Snippets are typed exactly
    as written and are never sent for rewriting."*
- **The Add button sits above the rows, not below them.** It is the thing a new user needs and it must
  not move down the page as the list grows. Labels: *Add a spelling*, *Add a replacement*, *Add a
  snippet*.
- **The section container uses `Width="520"`, `HorizontalAlignment="Left"`** — `Width`, not
  `MaxWidth`, on the container and every input inside it (gotcha 46). The wrapped hint text is the one
  place `MaxWidth` is correct.
- **Rows are an `ItemsControl`, not a `ListBox`.** No selection is needed, and a `ListBox` brings its
  own `ScrollViewer`, which would make a second scroll region inside the page's shared one (gotcha 52,
  and the same mistake the history window already paid for). Tens of entries do not need
  virtualisation.

### A row, populated state

One `Border` per entry, `Ui.ListRow` (new style, §7): transparent background, 1px `Ui.Border` bottom
only, `Padding="12,9"`, hover fills with `Ui.CardHover`.

| List | Row content |
|---|---|
| Spellings | The word, `Ui.Text`, single line. |
| Replacements | `heard` in `Ui.TextMuted`, a small arrow glyph in `Ui.TextMuted`, then `typed` in `Ui.Text`, in one `WrapPanel` so a long pair wraps rather than clips. |
| Snippets | First line: the trigger in `Ui.Text`, prefixed by the literal words the user has to say — **`insert my signature`** — so the row teaches the invocation. Second line: the snippet text in `Ui.TextMuted`, `FontSize="11.5"`, `TextTrimming="CharacterEllipsis"`, **single line**, with hard line breaks shown as a middle-dot separator rather than `\n`. The full text is in the row's `ToolTip`. |

On the right of every row, revealed on hover and always present in the tab order: two small icon
buttons, *Edit* (pencil) and *Remove*. Keeping them in the tab order while hiding them visually until
hover is deliberate — a hover-only control that is also unreachable by keyboard is not a control.

- Clicking anywhere else on the row also opens the Edit modal. Double-click is not required.
- **Remove deletes immediately, with no confirmation**, matching "deleting one entry does not ask;
  Clear still does". Nothing here is destructive until Save, and Cancel discards the lot.
- Every row item type (`SpellingRow`, `ReplacementRow`, `SnippetRow`) **overrides `ToString()`**
  (gotcha 42). The Edit and Remove buttons get `AutomationProperties.Name` of `"Edit tawk type"` /
  `"Remove tawk type"`, so a screen reader does not hear eight identical "Remove" buttons.

### Empty state

In place of the rows, one `Ui.Hint` line inside a `Ui.CardSurface` border with `Padding="16,14"`, so
the empty section is a visible shape rather than a gap:

- Spellings: *"No spellings yet. Add the names TawkType keeps getting wrong and they will come out
  right from the next dictation."*
- Replacements: *"No replacements yet. Add one when a dictation gives you the wrong words rather than
  the wrong spelling."*
- Snippets: *"No snippets yet. Add text you type often — a signature, an address, a template — and say
  "insert" and its trigger to have it typed for you."*

Each explains why the thing exists, not just that the list is empty. A `CountToVisibility` converter
is needed beside the existing ones in `Views/Converters.cs`.

### Footer

Below the three sections, with the two existing buttons plus a new hint:

```
[Import...]  [Export...]
Your vocabulary is kept in its own file, separate from the rest of your settings, so you can copy it
to another machine or keep it in a note. Importing replaces all three lists; nothing is saved until
you press Save.
```

That last clause is worth stating because import today silently replaces everything and only flashes
*"Vocabulary imported. Save to keep it."* after the fact.

## 2. The Add/Edit modals

Three dialogs, not one. A single dialog with a type picker would make every add a two-step, and the
three have genuinely different shapes.

All three are `Window`s in `src/TawkType.App/Views/`, modelled on `RememberWindow.xaml`: `Owner` set
to the Settings window, `ShowDialog()`, `WindowStartupLocation="CenterOwner"`, `ShowInTaskbar="False"`,
`Background="{DynamicResource Ui.Window}"`, and `TitleBarTheme.Apply(...)` in `OnSourceInitialized`.
Because they are shown with `ShowDialog`, `IsCancel="True"` **does** work here — gotcha 24 applies to
the modeless Settings window, not to these. No hand-wired Escape is needed, and none should be added.

Gotcha 38 matters only for whoever writes a UI Automation script against these: an owned dialog is a
descendant of the Settings window, not a child of `RootElement`. Worth a one-line comment in each
code-behind.

Each dialog returns its result by exposing a property the caller reads after `ShowDialog()` returns
true, rather than mutating the view model itself — the same shape as `RememberWindow` but without the
view-model dependency, so the dialogs stay dumb and the rules stay in Core (§7).

### 2a. Add/Edit a spelling

Title: *Add a spelling* / *Edit a spelling*. `Width="420"`, `SizeToContent="Height"`,
`ResizeMode="NoResize"`.

```
Add a spelling
Write the word exactly as you want it typed. TawkType matches it however
it was heard or capitalised, and types this.

Word
[________________________________]
Example: Jupitor Studio

[error line, Ui.FieldError, collapsed]

                                   [ Cancel ]  [ Add spelling ]
```

Buttons say *Add spelling* / *Save changes*, never "OK". Primary is `Ui.PrimaryButton` with
`IsDefault="True"`; Cancel is `Ui.Button` with `IsCancel="True"`.

Keyboard: focus lands in Word on open; Enter saves; Escape cancels; Tab order is Word → Cancel →
primary.

### 2b. Add/Edit a replacement

Title: *Add a replacement* / *Edit a replacement*. `Width="460"`, `SizeToContent="Height"`.

```
Add a replacement
TawkType will make this change to every dictation from now on, whether or
not the Claude pass is switched on.

When you hear
[________________________________]
What you say, as the recogniser writes it down: see sharp

Type this instead
[________________________________]
What should be typed in its place: C#

[error line]

                                   [ Cancel ]  [ Add replacement ]
```

The lead sentence is lifted verbatim from `RememberWindow`, and the two field labels are the same as
its — *When you hear* / *Type this instead* — because the same user meets both and they should not be
two vocabularies for one idea. The per-field hints are new and carry the examples, which is where `=>`
used to live.

Focus lands in *When you hear* for an Add, and in *Type this instead* (selected) for an Edit reached
from a row, matching `RememberWindow`'s reasoning that the second half is the one being changed.

### 2c. Add/Edit a snippet

Title: *Add a snippet* / *Edit a snippet*. `Width="520" Height="460"`, `MinWidth="420"
MinHeight="360"`, `ResizeMode="CanResizeWithGrip"` — this is the one dialog whose content has no
natural height, and a snippet the user cannot see all of is the exact failure the `\n` escape already
causes.

```
Add a snippet
Saved text, typed exactly as written. Snippets skip the Claude pass, so a
signature or a template comes out the way you wrote it.

Trigger
[________________________________]
You will say: insert my signature      <- live, updates as they type

Text to type
[                                ]     AcceptsReturn, MinHeight 160
[                                ]
Press Enter for a line break. Whatever you type here is typed exactly,
line breaks and all.

[error line]

                                   [ Cancel ]  [ Add snippet ]
```

The *You will say: insert my signature* line is a live preview bound to the trigger box, falling back
to *"You will say: insert and then your trigger"* while it is empty. It is the single most valuable
piece of text on this page: the `insert` prefix is invisible in the current design and nothing tells
the user about it until they read a hint three lines long.

Keyboard, and this is the one place it differs:

- Escape cancels (`IsCancel`) from anywhere, including inside the text area.
- Enter inside **Trigger** moves to the text area rather than saving — a dialog whose default button
  fires while the user is halfway through composing a signature is worse than no default at all.
  Implement by handling `PreviewKeyDown` on the trigger box.
- Enter inside **Text to type** inserts a line break, because `AcceptsReturn="True"`. This is the
  whole point.
- **Ctrl+Enter saves** from anywhere in the dialog, wired as an `InputBinding` on the window. The hint
  under the buttons says so: *"Ctrl+Enter saves."*
- The primary button therefore does **not** carry `IsDefault="True"` in this dialog. Stating it
  plainly so nobody "fixes" it later: a snippet dialog with a default button eats the user's line
  breaks.
- Tab order: Trigger → Text → Cancel → primary. `AcceptsTab` stays false so Tab leaves the text area.

### What happens on save

The dialog calls the Core reducer (§7), which returns either a problem sentence or the new list. On a
problem, the error `TextBlock` becomes visible and focus moves to the offending field; the dialog
stays open. On success the dialog closes, the view model replaces its collection, the section count
updates, and the Settings status bar flashes *"Replacement added. Save to keep it."*

The *"Save to keep it"* half matters: everything on this page edits the draft, and the existing import
flash already sets that precedent. Nothing writes to disk until the page's own Save.

New entries are appended to the end, not sorted. The list's order is the user's, it round-trips
through text mode, and re-sorting it under them would make a pasted list unrecognisable. Order does
not affect matching — `PhraseBook` sorts longest-first at apply time regardless.

## 3. How bulk editing survives

**The affordance:** a borderless text button at the right of each section header, reading *Edit as
text*. In text mode it reads *Done editing text*, the Add button and the rows are hidden, and the
section shows:

```
[ multi-line TextBox, Width 520, MinHeight 140, AcceptsReturn, monospace ]
[Ui.Hint]  One per line, as  heard => typed.  Paste a list here, sort it, or copy the
           whole thing into a note. Press Done editing text to go back.
[Ui.FieldError, collapsed]
```

The syntax hint only appears in text mode, which is the one place it is true and the one place it is
needed. The box uses `Consolas` at 12.5 so pasted columns line up; **no fixed `Height`**, `MinHeight`
only (gotcha 16).

Three rules make the round trip lossless:

1. **Entering text mode formats from the live collection.** No text is stashed and reused — there is
   no second copy to go stale.
2. **Leaving text mode parses, and is refused if any non-blank line is not an entry.** The error, in
   `Ui.FieldError` under the box, names the line: *"Line 4 is not a replacement — it needs the words
   you hear, then =>, then what should be typed. Fix it, or remove the line."* Today those lines are
   silently dropped; that is acceptable for a box the user is staring at and unacceptable the moment
   the text becomes a projection of something else. Blank lines are ignored, as now.
3. **Text mode blocks Save the same way a bad number does.** The existing `SaveBlockedBy` / `CanSave`
   machinery already does exactly this job for `CleanupTimeoutText` and friends — add the vocabulary
   parse errors to it, with `[NotifyCanExecuteChangedFor(nameof(SaveCommand))]` on each text property.
   Save with a clean text box parses and commits normally; the user does not have to press *Done
   editing text* first.

Text mode is per section, not per page — a user pasting fifty replacements should not have their
snippets torn out from under them. Mode is transient view-model state, not persisted; reopening
Settings opens on the lists.

Import keeps working unchanged, and now refreshes the collections instead of the strings. Export still
calls `ApplyVocabularyEdits`, which becomes "flush any open text box into the collections" and is a
no-op when no text box is open.

## 4. Validation and conflict rules

All of it lives in Core (§7); the dialogs only display sentences. Matching is case-insensitive and
longest-first, which drives most of these.

**Shared**

| Rule | Behaviour | Wording |
|---|---|---|
| Empty field | Blocked | *"Both halves are needed: what you said, and what it should be."* (existing wording, reused) — spellings: *"A spelling needs a word."*; snippets: *"A snippet needs a trigger and some text."* |
| Whitespace | Trimmed on both halves. Interior runs left alone — `PhraseBook` already matches with flexible spacing, so normalising the user's typing would be a change they did not make. | — |
| Too short | `From` / trigger / spelling under 2 characters is blocked, as `VocabularyEdit.MinimumHeardLength` already does. | *"That is too short to be a phrase — a one-letter rule would fire inside ordinary words all day."* |

**Replacements**

- **Duplicate `From`, case-insensitively:** on **Add**, the existing entry is offered for overwrite
  rather than a second rule being appended, because two rules for one phrase leave the user unable to
  tell which wins. The error line becomes a question and the primary button becomes *Replace it*. On
  **Edit**, a collision with an entry *other than the one being edited* is blocked outright.
- **Identical halves:** blocked, existing wording: *"Those are the same — there is nothing to
  correct."* Compared case-sensitively, since "tawktype => TawkType" is a real and useful rule.
- **A loop with an existing rule** — this entry's `To` is another entry's `From` — is a **warning, not
  a block**, with the primary button still enabled. Blocking would be wrong; it is occasionally what
  someone means.
- **Case:** stored verbatim. The `To` side is inserted exactly as written, and that is the documented
  point of the feature.

**Snippets**

- **Duplicate trigger, case-insensitively:** same overwrite-on-add, block-on-edit pair.
- **A trigger that is also a replacement's `From`** is a warning: *"You also have a replacement for
  "my signature". Snippets run first, so this one wins when you say "insert"."* True to
  `PhraseBook.Apply`, which runs snippets before replacements.
- No warning for one trigger being a prefix of another — longest-first ordering already makes the
  longer one win, which is what anyone would expect.
- Empty snippet text is blocked; whitespace-only likewise. Trailing blank lines are kept, because a
  signature that ends with a blank line usually means it.

**Spellings**

- **Duplicate, case-insensitively:** blocked, not overwritten, because there is no second half to
  overwrite. If the case differs, say so and offer the overwrite.
- A spelling that is only punctuation, or contains a line break, is blocked: *"A spelling is one word
  or phrase on one line."*

## 5. Snippets: storage and display

- **Stored** in `settings.json` and in the exported vocabulary file as `Snippet(Trigger, Text)` with
  **real line breaks** in `Text`. JSON encodes them itself; the user never sees that, and
  `VocabularyFile` already round-trips it correctly today. No storage change is needed.
- **In the modal**, a real multi-line `TextBox` with `AcceptsReturn="True"`. The literal two-character
  escape `\n` is never produced by this path and never shown to the user.
- **In a row**, multi-line text is collapsed for display only, full text in the tooltip. A row is an
  index, not an editor.
- **In text mode**, and only there, `VocabularyFormat.Escape` still applies — one entry per line means
  line breaks have to be written out, and the existing handling and its tests stay exactly as they
  are.
- **One real bug this exposes:** `VocabularyFormat.Escape` maps `\r\n` and `\n` to the same escape and
  `Unescape` expands to `Environment.NewLine`, so a snippet that came in through import with bare
  `\n` line endings comes back out of a text-mode round trip with `\r\n`. Harmless for typing, but it
  makes the existing round-trip test pass only because its fixture uses `Environment.NewLine`. Worth
  a test that starts from `"a\nb"` and asserts the text is unchanged through a list → text → list
  round trip.

## 6. Build order

**Slice 1 — Replacements only, end to end.** Core reducers and tests, the row view model, the section
skeleton with Add button, rows, empty state, the `AddReplacementWindow` modal, and the *Edit as text*
toggle with its parse gate. Spellings and Snippets stay exactly as they are — three sections that do
not yet match is a visibly transitional page, but it ships, it solves the `=>` half of the complaint,
and every later section is a copy of a thing already proven in the app.

**Slice 2 — Snippets.** The same skeleton, the `AddSnippetWindow` with its multi-line field and
Ctrl+Enter, the two-line row, the `insert` preview. This is where the `\n` complaint dies. Second
rather than first because it is the harder dialog and the pattern should be settled before it is
written.

**Slice 3 — Spellings.** Simplest of the three and last, because the current text box is least harmful
for a one-per-line list. Its modal is one field.

**Slice 4 — the polish that only matters once the lists are real.** Counts in the header wired to live
collection changes; the loop and snippet-versus-replacement warnings; a filter box that appears on a
section once it holds more than about twenty entries; `Import...` gaining a *Merge* option beside
*Replace*.

The three sections can ship in three separate releases; nothing in slices 2 and 3 changes anything
slice 1 built.

## 7. Core changes, and the new styles

Everything with a rule in it goes to `TawkType.Core/Settings/`, with tests, because that is where this
codebase already puts awkward logic and because every one of these rules is a way to quietly damage
the user's vocabulary.

### `VocabularyEdit` — extended, not replaced

`Learn` stays exactly as it is; `RememberWindow` and the history path depend on it. Add beside it:

```
VocabularyLearned Upsert(IEnumerable<TextReplacement> existing, int? editing, string? from, string? to)
SnippetEdited     UpsertSnippet(IEnumerable<Snippet> existing, int? editing, string? trigger, string? text)
SpellingEdited    UpsertSpelling(IEnumerable<string> existing, int? editing, string? word)
VocabularyWarning[] Warnings(VocabularySettings vocabulary, <the entry about to be saved>)
```

`editing` is the index being edited, or null for an add — the whole reason `Learn` cannot be reused
directly is that editing an entry must not collide with itself. Each returns the same record shape as
`VocabularyLearned`: the new list, a `Problem` sentence or null, and a `Replaced` flag. `Warnings`
returns the non-blocking ones separately so the dialog can show them without them gating the button.

New tests: editing an entry to its own value is allowed; editing onto another entry's phrase is
refused; adding onto an existing phrase overwrites and reports `Replaced`; case-insensitive collision
is found; a trailing-space phrase collides with the trimmed one; a two-rule loop produces a warning
and not a problem; a snippet trigger matching a replacement produces a warning; each too-short and
empty case produces its sentence.

### `VocabularyFormat` — one addition

```
readonly record struct VocabularyParse<T>(T[] Entries, int[] RejectedLines)
VocabularyParse<TextReplacement> ParseReplacementsStrict(string? text)
VocabularyParse<Snippet>         ParseSnippetsStrict(string? text)
string[]                         ParseSpellings(string? text)   // the split currently inlined in the view model
```

`RejectedLines` are 1-based line numbers, which is what the error message needs. The existing
`ParseReplacements` / `ParseSnippets` stay and become one-line wrappers over the strict versions, so
nothing that calls them changes and their tests keep passing. `ParseSpellings` moves the `Separators`
split out of `SettingsViewModel` into a tested function.

Tests: a line with no separator is reported with its number and not swallowed; blank lines are not
rejected; a rejected line does not stop later good lines being parsed; line numbers are right when the
text uses `\r\n`; the list → text → list round trip is the identity for entries containing `=>` in the
right-hand side, backslashes, and line breaks.

### View model

`SettingsViewModel` gains three `ObservableCollection<T>` row lists seeded in the constructor from the
draft, three `bool` text-mode flags, three text strings, three parse-error strings folded into
`SaveBlockedBy`, and commands `AddReplacement` / `EditReplacement(row)` / `RemoveReplacement(row)` and
the two parallel sets. `ApplyVocabularyEdits` changes from "parse three text boxes" to "flush any open
text box, then write the three collections into the draft", and keeps its two existing callers
unchanged.

**One thing not to miss:** `VocabularyText` is bound in two places — the Vocabulary page *and* the AI
cleanup page's *Personal dictionary* field. When spellings become a collection in slice 3, that second
binding has to become the text projection or be removed. It works today only because `Separators`
includes a comma.

The Modes page's *Words this mode adds* text box stays a text box. It is a different list on a
different page and nothing about it is in the complaint.

### Styles and resources

Two new styles in `Themes/Controls.xaml`, and **no new brushes**, so `Light.xaml` and `Dark.xaml` are
untouched and gotcha 17 does not apply:

- `Ui.ListRow` (`TargetType="Border"`) — `Background="Transparent"`, `BorderBrush="{DynamicResource
  Ui.Border}"`, `BorderThickness="0,0,0,1"`, `Padding="12,9"`, hover to `Ui.CardHover`.
- `Ui.LinkButton` (`TargetType="Button"`, `BasedOn="{StaticResource Ui.Button}"`) — the *Edit as text*
  toggle and the row's Edit/Remove: no border, no fill, `Ui.TextMuted` going to `Ui.Accent` on hover.

Both use `DynamicResource` against keys that already exist in both themes. Name-check against gotcha
15 before adding either: a key is a brush or a style, never both.

Also needed: a `CountToVisibility` converter in `Views/Converters.cs`. Icons: `Icons.Close` already
exists for Remove; a pencil geometry needs adding to `Views/Icons.cs`.
