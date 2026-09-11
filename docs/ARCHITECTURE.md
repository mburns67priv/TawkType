# Architecture

## Why this stack

Decided against the target machine: Windows 11, RTX 4060 Ti (8 GB), i7-11700F, 32 GB RAM, .NET 8 SDK and
Visual Studio 2022 already installed, no Rust toolchain, no CUDA Toolkit.

**.NET 8 + WPF** over Tauri / Electron:

- Push-to-talk needs key-*up* events from any app. `RegisterHotKey` only reports presses, and Tauri's
  global-shortcut plugin has the same gap. A `WH_KEYBOARD_LL` hook is 80 lines of P/Invoke in C#.
- Text injection is `SendInput` with `KEYEVENTF_UNICODE`. Native call, no bridge.
- A transparent, always-on-top, click-through, never-activating overlay is standard WPF.
- Whisper.net ships prebuilt native runtimes as NuGet packages. No C++ build, no Python sidecar, one
  process, ~40 MB idle instead of Electron's ~150 MB.
- Packaging later is MSIX or Velopack; both are first-class for .NET desktop apps.

**Two speech engines**, selected by `EngineSelection.Resolve(engine, language)` in Core and routed by
`TranscriberRouter` in the Transcription project:

- **Parakeet TDT 0.6B v3** (NVIDIA, CC-BY-4.0) through sherpa-onnx, int8 on CPU. Default for English
  and the other 24 European languages it covers. Transducer architecture, so it is several times faster
  than Whisper, scores a lower English word error rate, and does not hallucinate on silence. ~670 MB.
- **Whisper large-v3-turbo** through Whisper.net for the other ~75 languages. ~1.6 GB. Runtime order is
  CUDA 12 → Vulkan → CPU; CUDA needs the toolkit installed, Vulkan works with the stock driver.

Both engines stay registered; only the one the router selects is loaded, and it loads lazily.

## Pipeline

```
IPushToTalkHotkey ──Pressed──► DictationEngine ──► IFocusProbe (on a pool thread)
                                               ──► IAudioCapture.Start()
                  ──Released─►                 ──► IAudioCapture.Stop() ──► AudioClip
                  ──Cancel───►                 ──► session cancelled, nothing kept
                                               ──► ITranscriber.TranscribeAsync ──► TranscriptResult
                                               ──► PhraseBook.Apply (local vocabulary)
                                               ──► ITextCleaner.CleanAsync ──► string
                                                     └─► ILlmClient.CompleteAsync (optional, timed out)
                                               ──► PhraseBook.Apply again (a rewrite must not undo it)
                                               ──► Recognised event  ← history is written here
                                               ──► IFocusProbe revalidation
                                               ──► ITextInjector.InjectAsync
```

Two orderings in there are deliberate and were both bugs once. **The words are announced before they
are delivered**, so a dictation that fails to type is still in the history rather than lost with the
exception. And **the phrase book runs again after the rewrite**, because the model is given the raw
transcript and would otherwise quietly undo every correction the user has written down.

`DictationEngine` (in `Talk2Me.Core`) owns the state machine:

```
Idle ──press──► Listening ──release──► Transcribing ──► [Polishing] ──► Injecting ──► Idle
                    │                       │
                  Esc ──► Idle           failure ──► Error ──► Idle
```

Rules: presses while busy are ignored; taps shorter than `MinimumHoldMs` are dropped; the release handler
hops off the hook thread immediately because low-level hooks are killed by Windows if they stall.
`Polishing` is entered only when `ITextCleaner.MayTakeAWhile` says the cleaner will call out to a model,
so the fast regex path does not flash a state the user cannot read.

Every stage is an interface so each can be swapped independently:

| Interface | Today | Later |
|---|---|---|
| `IPushToTalkHotkey` | keyboard hook | mouse side-button, foot pedal, double-tap toggle mode |
| `IAudioCapture` | WaveIn 16 kHz | WASAPI shared-mode with own resampler, VAD auto-stop |
| `ITranscriber` | router → Parakeet (sherpa-onnx) or Whisper.net | streaming partials while the key is held, Parakeet on GPU via a CUDA sherpa-onnx build |
| `ITextCleaner` | regex fillers, then an optional Claude rewrite | tone chosen per foreground app; a local model behind the same `ILlmClient` |
| `ILlmClient` | `ClaudeLlmClient` (Anthropic SDK) | llama.cpp / ONNX for an offline rewrite |
| `ITextInjector` | SendInput / clipboard | UI Automation `TextPattern` for exact caret insertion |
| `IDictationHistory` | JSONL append under the profile | search, pinning, re-inject a past dictation |
| `IWindowActivator` | `SetForegroundWindow` + settle poll | nothing planned; it exists for the dictation box |
| Settings UI | nav rail + six pages, themed | per-page validation, an onboarding flow on first run |

## LLM cleanup

`LlmTextCleaner` is the registered `ITextCleaner`. It always computes the regex result first, then tries
to replace it with a rewrite. Everything about that attempt is best-effort:

- the pass is skipped entirely unless `Cleanup.UseLlm` is on **and** `ILlmClient.IsConfigured`
- the call runs under a `CancellationTokenSource.CancelAfter(Cleanup.TimeoutMs)`, 2 s by default
- `ClaudeLlmClient` sets `MaxRetries = 0` — a retry inside that budget only delays the fallback
- a reply that is empty, or more than 3× the transcript plus 60 characters, is discarded
- `<transcript>` / `</transcript>` wrappers and markdown fences are stripped if the model adds them
- timeout, transport failure, auth failure, refusal — all log and return the regex text

The last two points are the injection guard. Dictated speech is untrusted input that reaches a model
whose entire output is typed into the user's focused window, so the prompt fences the transcript and
states that it is never an instruction, and the length check catches a model that complied anyway.

`Talk2Me.Core` has no provider SDK reference: `ILlmClient` is a two-method interface and
`ClaudeLlmClient` lives in `Talk2Me.Llm`. A local-model client implements the same interface.

The API key never enters `settings.json`, which is plain text. `IApiKeyStore` keeps it in
`%LOCALAPPDATA%\Jupitor Studio\Talk2Me\apikey.dat`, encrypted by `DpapiApiKeyStore` with DPAPI under the current user
(`ANTHROPIC_API_KEY` is the fallback). That protects the file at rest against other accounts on the
machine — not against anything running as this user.

## Starting and stopping

`Hotkey` (Core) parses `"Ctrl + Shift + D"` into modifiers plus a trigger, and is the one place that
knows key names. No key it can name contains a `+`, because the parser splits on it — the numpad
operators are "Numpad Plus", "Numpad Minus" and so on, with the old spellings kept as aliases, and a
test walks every name asserting both halves of that.

`HotkeyGesture` turns the raw hook stream into presses and releases, and `Activation` layers the three
ways a dictation can begin or end on top of it. Both are pure reducers in Core with no Win32 anywhere
near them, which is the only reason the awkward cases have tests at all:

- **Auto-repeat.** Holding a suppressed trigger produces a stream of key-downs, not one. Only the first
  is a press; the rest are swallowed and ignored, or a held letter types itself.
- **A release with no press.** A key swallowed on the way down must have its release swallowed too,
  and a release the gesture never saw the press for is not the end of anything.
- **The hotkey changing mid-hold.** The gesture is reset rather than left waiting for a release nobody
  is watching for.
- **Toggle.** An optional second key where a press each starts and stops. Held keys and the toggle
  coexist; the toggle is reset by a cancel, or the next press would stop a dictation that never began.
- **Escape.** Only while a dictation is actually in progress, so Esc keeps its normal meaning the rest
  of the time.

The recording limit (`MaxRecordingSeconds`, five minutes, 0 for none) lives in `DictationEngine` as a
timer that raises the same release the key would. Deliberately a *finish* rather than a cancel:
whatever was said before the limit is worth more than the silence after it.

## Sessions

Every dictation is a `Session` with an id, a `CancellationTokenSource` and the task doing the work.
Before it existed, a cancelled or superseded dictation had no way to stop the transcription already
running, and shutdown could tear the process down mid-write.

- Work checks the token at each stage, so Escape stops the pipeline rather than only hiding its result.
- Only the session that owns the state machine may change it, so a late-finishing dictation cannot
  stamp on the one that replaced it.
- `Stop()` cancels and then waits up to `ShutdownGrace` (3 s) for the work to unwind, so history and
  the clipboard are left in one piece.
- State changes are raised outside the lock. Raising them inside it deadlocked against handlers that
  called back in.

The focus probe result is taken at key-down but **re-checked at delivery**, because a dictation can
easily outlive the window the user was aiming at. If the target has changed, the text goes to the
clipboard rather than into whatever happens to be in front now.

## The dictation box

"Copied to your clipboard" is only half a recovery. The user still has to find the words, decide
whether they are right, and get them somewhere — and if anything else touches the clipboard in
between, they are gone. The dictation box is where a dictation that did not arrive waits instead:
editable, and for as long as they want it.

`Recovery.For` (Core, pure) turns a finished dictation into the offer made for it — the headline, the
explanation, and which of three routes is honest:

| Route | When | What the box offers |
|---|---|---|
| `None` | it was typed | nothing; no banner at all |
| `SendBack` | the target window is still identifiable and not elevated | **Send it back**, plus Copy |
| `CopyOnly` | elevated target, or no window to aim at | Copy, and a sentence saying why that is all |

The elevated case is the one worth being careful about. Windows discards synthetic input aimed at a
more privileged process and reports nothing, so a second attempt would fail exactly as silently as the
first. Offering the button anyway would be worse than not having it — so the box says plainly that the
text has to be pasted by hand.

**Send it back** hands the foreground to the remembered window through `IWindowActivator` and then
types into it. That works only because the user pressed a button in a window that is already in front:
Windows lets a process *give away* the foreground, never take it. `Win32WindowActivator` restores a
minimised target first, then polls until the switch has actually happened — `SetForegroundWindow`
returns before it has, and typing into a window that is not yet in front loses the first characters.
A window that has gone, or a switch Windows refuses, is an ordinary outcome: the box says so and falls
back to Copy.

**It never opens itself.** The bar reports the failure and grows a button; the box appears when it is
asked for. A window that appeared over whatever the user was typing into would be a worse interruption
than the delivery that just failed — and it would take the focus that the rest of this application
works so hard never to touch.

## The phrase book

`PhraseBook.Apply` is the local half of personalisation, and it runs whether or not there is an API
key — the vocabulary used to be nothing but words in a prompt, so a local-only user got nothing from
it at all.

Three lists, applied in this order: **snippets**, then **replacements**, then **spellings**.

- **Snippets** are saved text behind a trigger, and need the word "insert" in front of it. Explicit,
  or a signature appears in the middle of an ordinary sentence. A dictation that expanded one
  short-circuits the LLM pass entirely — a model asked to tidy up a signature would do exactly that —
  and a multiline one reaches the clipboard path rather than being typed as Return presses.
- **Replacements** are `heard => typed`, for the mishearings a spelling cannot fix.
- **Spellings** are names that should come out as written however they are heard.

Matching is whole phrases, case-insensitive, tolerant of the recogniser's spacing, and longest-first,
so "Jupitor Studio" wins over "studio" and "restudio" is left alone. Boundaries are
`(?<![\p{L}\p{N}])…(?![\p{L}\p{N}])` rather than `\b`, which gets punctuation and accented letters
right where `\b` does not.

Both lists are edited as plain text, one `heard => typed` per line, rather than a grid of rows with add
and remove buttons: they are written in bursts, usually pasted from somewhere, and plain text can be
selected, sorted, diffed and kept in a note. `VocabularyFile` exports and imports the whole vocabulary
as JSON of its own, because it is the part of Talk2Me that is genuinely the user's own work and should
not be trapped in a file full of window positions. A file carrying none of the three lists is refused
rather than imported as an empty one over the top of theirs.

## Settings validation

`NumberField` (Core) carries what each numeric box will accept — a range, a unit, and whether a
fraction makes sense — and turns a bad value into the sentence shown under the box. Save is disabled
until every box is happy, and when the offending box is on a page the user has navigated away from,
the footer names the page.

Nothing is clamped or silently corrected. The window used to drop an unusable value on the floor: the
box kept what was typed, the setting quietly stayed as it was, and Save closed looking like it had
worked. Clamping would be worse still, since a value the user never chose would then be saved under
their name.

## Knowing whether the text can land

Before a dictation is typed, `IFocusProbe` reports whether the focused element can actually take text.
`UiaFocusProbe` answers it from UI Automation — the accessibility tree — which is why it works across
Win32, WinForms, WPF, UWP, Chromium and Office rather than only classic edit controls. It also checks
whether the foreground process is elevated, because synthetic input to a higher-privilege window is
discarded by Windows with no error at all: the single best explanation for "my dictation went nowhere".

Two properties make it safe:

- **It runs at key-down, not key-up.** `DictationEngine.OnPressed` kicks it off on a pool thread (never
  on the hook thread, which has a tight time budget) and the answer is read when the key is released.
  The user is speaking for the whole of that, so the probe is free — and it captures focus as it was
  when they started talking, which is what they were aiming at. A 400 ms grace period at delivery time
  means a wedged accessibility tree can never hold up finished text.
- **It is permissive.** Only `NotEditable` and `Elevated` stop the text being typed; `Unknown` types as
  before. Accessibility data is patchy — some Java apps, games and custom-drawn editors expose nothing —
  and refusing to type into a field that would have worked is a worse bug than the one being fixed. The
  "definitely not text" control list is deliberately short for the same reason.

When it does stop, the text goes to the clipboard through `IClipboard` and the bar says *Copied instead*
with the reason. Nothing is lost, and the history records it with `CopiedNotTyped`.

`tools/Talk2Me.Focus` prints the verdict once a second so the behaviour can be checked against real
applications; that part cannot be unit tested, because it depends on what each application chooses to
expose.

## Modes

Changing a setting and choosing how the next dictation should behave are not the same act. A setting
is a decision made once; a mode is a decision made because of what is about to be said, and it has to
be switchable in the second before saying it.

`DictationMode` is a named bundle of the post-processing choices, and **every one of them is local**:
filler removal, whether to capitalise, the trailing space, caret fitting, and a vocabulary of its own.
Four ship built in:

| Mode | What it does, with no model involved |
|---|---|
| **Literal** | No fillers removed, no capital added. Preserves what the recogniser produced — which is not a promise that everything said was recovered, and no mode can make it one. |
| **Clean prose** | The old defaults: fillers out, sentence capital, caret fitting on. |
| **Chat** | Fillers out, no forced capital, no trailing space. |
| **Technical** | No forced capital, `Verbatim` tone, and its own word list. |

The one setting a mode cannot reach is whether text leaves the machine. `MayUseLlm` can only ever
**narrow** `Cleanup.UseLlm` — unticking it keeps a mode local whatever the AI cleanup page says, and
ticking it switches nothing on. Picking a mode must never be the act that authorises sending anything
anywhere, so `LlmTextCleaner` checks both and there is a test for each direction.

A mode's vocabulary is **added to** the main one rather than replacing it, and goes first so the more
specific entry wins where both name a phrase. A correction the user has taught Talk2Me should not stop
applying because they picked a different mode.

Without a model, "preserve identifiers and acronyms" splits into three, and two of them survive:
snippet text was already exact, acronyms are a rule rather than a judgement — an all-capitals word is
never treated as a filler, so *"The ER is open"* keeps its department — and identifiers become a word
list the user maintains. That is narrower than a model inferring them, and it is the part that matters
day to day, because the identifiers anyone dictates are a small recurring set.

The cycling key is a third gesture in the same `Activation` reducer, so it inherits suppression,
auto-repeat and orphaned releases for free. It deliberately does nothing to a dictation already
running: changing how the words will be treated halfway through saying them is not something anyone
means. Switching **saves**, because a mode nobody can see the state of after a restart is worse than
one extra settings write — and the bar carries the mode's name for the same reason.

## Fitting the text to where it lands

A dictation used to be typed exactly as the cleaner produced it, plus an unconditional trailing space.
That is wrong in the middle of a sentence in three visible ways: a doubled space where one was already
there, no space where one was needed, and a capital letter the cleaner added to what is actually a
continuation.

`IFocusProbe.ReadCaret` reads up to 64 characters either side of the caret through UI Automation's
`TextPattern`, and whether anything is selected. It is a **separate call from `Probe`, made at delivery
time**, because the caret is precisely the thing that moves while a dictation is being transcribed — it
cannot be answered at key-down like the rest of the probe. That makes it the one accessibility call on
the path of finished text, so it gets a 250 ms budget and answers "do not know" the moment it runs out.
Nothing the user has already said is ever held up waiting on somebody else's message loop.

`CaretFit.Fit` (Core, pure) then decides three things, and every one of them only fires on evidence:

- **A separating space**, unless what precedes the caret is already whitespace, the start of the field,
  or something that opens a phrase — `(`, a quote, a hyphen, a slash.
- **A trailing space**, unless the text after the caret already begins with whitespace, or begins with
  punctuation that belongs to the sentence being joined. `the deadline , which` is worse than no space.
- **The first letter's case.**

Capitalisation is the one that could do real damage, and it is safe only because of a narrow rule:
**Talk2Me will undo a capital it added, and never one the recogniser produced.**
`WasCapitalisedByCleanup` compares the raw transcript with the finished text — lower there, upper here,
the same letter otherwise — which is exactly the edit `BasicTextCleaner` makes. Lowercasing a capital
the recogniser itself produced would turn somebody's name into a common noun, and no amount of
surrounding context can tell those two cases apart after the fact.

A selection is not a caret. When text is selected the insertion replaces it, and the words either side
are already spaced for what was there — so nothing is added at either end, and nothing is lowercased.

`CaretContext.Unknown` is an ordinary outcome, not a failure: plenty of controls expose `ValuePattern`
and no `TextPattern`, meaning they can say what they hold but not where the caret is in it. Unknown
produces exactly the behaviour Talk2Me had before any of this existed — the configured trailing space
and nothing else — and `Activation → Fit the text to where it lands` turns the whole thing off.

`tools/Talk2Me.Focus` prints the caret context beside the verdict, because which applications answer
this is not something that can be unit tested. Measured: Chromium edit controls and WPF text boxes
both answer in under 10 ms.

## The taskbar button

Talk2Me keeps a taskbar button for as long as it runs, and `TaskbarWindow` exists only to hold it. The
status bar cannot: it is a `WS_EX_TOOLWINDOW` and it hides whenever it is minimised or set not to rest on
screen, which would take the button away at exactly the moment the user needs something to click.

So `TaskbarWindow` is one pixel, parked at -32000,-32000 and permanently minimised — never seen, only its
button is. Restoring it (a taskbar click, or Alt+Tab) is the signal: bring the status bar back, then drop
straight back to minimised so the button stays. Closing it from the button's system menu quits the app,
which is what closing a taskbar button means; it routes through `Application.Shutdown` rather than just
destroying the window, or Talk2Me would keep running with no button.

It deliberately has a normal window style. `WindowStyle="None"` with `AllowsTransparency` — the obvious
choice for a window meant to be invisible — stops WPF applying `Window.Icon`, and the taskbar falls back
to a generic icon. Off-screen and minimised is enough to keep it out of sight.

## Remembered window positions

The status bar and the history window both remember where the user put them, which on a multi-monitor
desktop is a trap. The obvious check — "is the saved point inside the virtual desktop?" — is wrong,
because the virtual desktop is the *bounding box* of the monitors and can contain large regions with no
monitor behind them. Restore a window into one of those and it is invisible and cannot be dragged back.

So `WindowPlacement` (Core, pure, unit tested) works against the real monitor rectangles: find the work
area the window overlaps most, clamp it fully inside, and return null when it overlaps none so the caller
falls back to a default that always exists — the configured corner of the primary screen for the bar,
centred on the primary for the history window. `MonitorLayout` (Talk2Me.Windows) supplies the work areas
from `EnumDisplayMonitors`.

Positions are stored and applied in **physical pixels**, captured with `GetWindowRect` and applied with
`SetWindowPos`. WPF's `Left`/`Top` are device-independent units, and converting between the two needs the
DPI of the monitor you are moving *to* — which you do not know until you are there. Staying in physical
pixels removes the conversion, and with it the mixed-DPI special cases.

Both windows also handle `SystemEvents.DisplaySettingsChanged`, so a window stranded by a monitor being
unplugged mid-session comes back immediately rather than at the next launch.

## Theming

`App.xaml` merges two dictionaries: slot 0 is the theme (`Themes/Light.xaml` or `Themes/Dark.xaml`),
slot 1 is `Themes/Controls.xaml`. `ThemeManager` swaps slot 0 at runtime; every colour that varies is a
`DynamicResource`, so open windows restyle in place rather than being reloaded.

Two rules keep that working:

- **The theme dictionaries must define exactly the same keys.** A key present in one and not the other
  fails only in that theme, and only at the moment a user switches to it.
- **A key is either a brush or a style, never both.** `Ui.Card` is the surface brush; the Border style is
  `Ui.CardSurface`. Sharing a name resolves to whichever the dictionary saw last and throws
  `'System.Windows.Style' is not a valid value for property 'Background'` when the window opens.

Controls are templated rather than themed by property alone, because WPF's stock ComboBox, CheckBox and
Button chrome is drawn from system colours and looks wrong the moment the app is dark. Inputs use
`MinHeight`, never a fixed `Height`: a fixed height starves the templated `PART_ContentHost` and the text
silently disappears while the control still reports its value.

`AppTheme.System` reads `HKCU\…\Themes\Personalize\AppsUseLightTheme` and keeps following it through
`SystemEvents.UserPreferenceChanged`.

## The overlay pill

The pill is the one window that is on screen while the user works in something else, so its whole design
is about not interfering:

- `WS_EX_NOACTIVATE` — Windows delivers clicks but never *activates* the window. This is what lets the
  bar have a toolbar and be dragged while the caret stays in the user's editor.
- `ShowActivated="False"` — showing it does not pull focus off the user's window.
- Raised with `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)`. Never `Activate()` or
  `SetForegroundWindow` — either would move focus, which is the one thing this window must not do.

`WS_EX_TRANSPARENT` and `IsHitTestVisible="False"` were how the pill used to guarantee it never
interfered; they are gone, because a click-through window cannot have buttons. `WS_EX_NOACTIVATE` alone
gives both halves, and there is a test for it: a real synthetic click on the bar's Copy button, with
`GetForegroundWindow()` compared before and after.

`Topmost="True"` alone is not enough: another topmost window put up later sits above it. So the pill
re-asserts its z-order every time it goes from resting to active, which is exactly when the user needs
to see it.

The bar carries its own toolbar — Settings, History, Copy last, collapse, and hide-between-dictations —
and can be dragged anywhere; `Overlay.WindowLeft`/`WindowTop` remember where, falling back to
`Overlay.Position` when the saved point is no longer on a connected screen. While listening it shows the
elapsed time and a level meter built from a rolling buffer of `IAudioCapture` samples, one `WaveBar` per
column so the bars animate without rebuilding the list.

Putting the bar away has two meanings, and they are separate state. `Overlay.AlwaysVisible` (persisted)
controls whether it rests on screen between dictations; `OverlayViewModel.IsHidden` (session-only) is the
minimise button and suppresses it entirely, dictation included. Both `Show` and `Settle` respect
`IsHidden`, so nothing in the pipeline can put a minimised bar back on screen. `Restore()` — what the
tray icon calls — clears both, because from the tray they look like the same problem.

Between dictations `OverlayViewModel` settles rather than hides: `IsResting` goes true, the text becomes
the hotkey hint, and the view fades the pill to `Overlay.RestingOpacity`. `Overlay.AlwaysVisible = false`
restores the old hide-when-idle behaviour.

## History

`DictationHistoryStore` (Core) appends one JSON object per dictation to
`%LOCALAPPDATA%\Jupitor Studio\Talk2Me\history.jsonl`. Append-only is the point: a dictation must never be lost or
delayed by the log, so the common path is one `File.AppendAllText`, and any failure there is logged and
swallowed — the text has already been typed by then.

The whole file is rewritten only when trimming past `History.MaxEntries` (plus slack, so a rewrite is not
on every add) or clearing. A line torn by a crash mid-write is skipped at load rather than failing the
file, and the next append terminates it rather than joining onto it.

Retention means the file, not the view. Capping `Recent` while leaving everything on disk is the bug
that made the setting untrue: "keep 50" has to delete the rest, not hide it. `Clear()` reports whether
the file actually went, so the UI cannot claim a deletion that failed, and compaction writes a temp
file and moves it over the original so an interrupted trim cannot lose the log.

The log is also where corrections are made, not just read. `Remove` and `Replace` rewrite the file
through the same temporary-file move as compaction — JSON Lines has no way to change a line in place,
which is the price of a format whose append path is one call and whose torn lines cost one record
rather than the file. Both report whether the write succeeded, and put the record back in memory when
it did not: a list that stops showing an entry still on disk is exactly the lie `Clear` exists to
avoid telling.

`HistoryQuery` filters on what was typed **and** what was heard, because the reason to go looking for
an old dictation is usually that it came out wrong — the words the user remembers saying may only
exist in the raw transcript.

Editing writes only `FinalText`. `RawText` is the evidence a correction is learned from, and an edit
that overwrote it would destroy the pair the vocabulary needs. That pair is what **Remember…** turns
into a replacement: `CorrectionGuess.Between` trims the words both versions agree on from each end, so
"send it to jupitor studio please" against "Send it to Jupiter Studio please." proposes *jupitor
studio → Jupiter Studio* rather than the whole sentence — a rule for a whole sentence only ever fires
on that sentence again. It compares words rather than characters, since a character diff of
"jupitor"/"Jupiter" proposes letters nobody can read or edit, and it ignores a leading capital only
when stepping over it still leaves a correction behind. `VocabularyEdit.Learn` then refuses the
degenerate cases: a phrase replaced by itself, a single character, or a second rule for a phrase that
already has one — which would leave the user no way to see which was winning.

**Clean again** re-runs `ITextCleaner` over the raw transcript under whatever the settings say now, and
puts the result in the draft rather than on disk: the point is to see what it would say, and accepting
it is a separate decision. It reprocesses text, never audio. Re-transcribing would mean keeping a
permanent archive of every recording ever made, which is not a thing to introduce by default.

`HistoryWindow` subscribes through `IDictationHistory.Changed`, so it updates live while it sits on
screen. A rebuild carries half-finished edits across, since dictations land while the user is typing
into a past one. Its list expands rows in place rather than pairing a list with a detail pane: in a 420x560 panel
two scroll regions fight over the height, and the loser gets clipped mid-line. One region, and a
selected row grows into a card with an accent spine, its full text selectable, what was heard beneath it
when cleanup changed anything, and Copy in the row header so a long dictation cannot push it out of
view. Closing it hides it; only shutdown really closes it. Its placement and always-on-top state live
in `HistorySettings` so a window meant to stay visible comes back where it was.

Everything dictated is therefore on disk in plain text under the user's profile. That is a deliberate
trade for recoverability, and `History.Enabled` turns it off.

## Threads

- **UI thread**: WPF, tray icon, the keyboard hook (hooks need a message loop), overlay updates via
  `Dispatcher.BeginInvoke`. `OverlayViewModel` also reacts to `ISettingsProvider.Changed`, which only
  ever fires from a `SettingsStore.Save` on this thread.
- **WaveIn callback thread**: appends samples under a lock, raises level events.
- **Thread pool**: transcription, cleanup, injection (`Task.Run` from the release handler).
- Whisper's processor is not thread-safe; `WhisperTranscriber` serialises calls with a semaphore.

## Settings

`%LOCALAPPDATA%\Jupitor Studio\Talk2Me\settings.json`, loaded once at startup by `SettingsStore` and re-read by consumers
through `ISettingsProvider.Current`, so a save takes effect without a restart: the hotkey re-resolves on
`Changed`, the transcriber reloads when model or language differ from what is loaded, audio device is
resolved at each `Start()`.

## Roadmap

1. ~~**LLM cleanup** behind `ITextCleaner`~~ — done, Claude-backed and off by default. Still open: a
   local backend behind `ILlmClient` for an offline rewrite, and streaming the rewrite so the first
   words are typed before the last ones arrive.
2. **Per-app styles**: detect the foreground window's process name, pick a tone preset (chat vs email vs
   code editor).
3. ~~**Personal dictionary**~~ — done locally: spellings, replacements and snippets apply with or
   without a model (`PhraseBook`), and the whole vocabulary imports and exports. Still open: seed
   Whisper's `initial_prompt` with it, and a "remember this replacement" action in the history window
   so a correction can be saved from the dictation that needed it.
4. **Command mode**: select text, hold a second key, speak an instruction, replace selection.
5. **Streaming**: transcribe in 1-second windows while the key is held so text appears as you speak.
6. **Branding, continued**: identity, icon, palette and overlay restyle are done (see
   `branding/BRAND.md`). Still to do: overlay waveform animation, onboarding window, installer (MSIX or
   Velopack), auto-update.
7. **Auto-start** with Windows, crash recovery. The single-instance guard is in (a `Local\` mutex in
   `App.OnStartup`).
