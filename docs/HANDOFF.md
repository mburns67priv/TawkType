# Talk2Me — handoff

Written 2026-09-10 at the end of the first build session; updated 2026-09-11 after the review and
feature passes. Read this first; then `README.md` for usage, `docs/ARCHITECTURE.md` for design,
`branding/BRAND.md` for the identity. `docs/REVIEW-2026-09-11.md` and
`docs/FEATURE-RESEARCH-2026-09-11.md` are the two assessments that drove most of what follows.

## What this is

Push-to-talk dictation for Windows, a Wispr Flow clone. Hold Right Ctrl, speak, release; the cleaned-up
text is typed into whatever has focus. Everything runs locally. There is no cloud, account, or telemetry.

Owner: Jupitor Studio. Working name was **Murmur**; it is now **Talk2Me**.

## State of the code

- **Branch `main`, clean tree.** Last release tag `v0.2.9`; several commits past it, so the next pack
  is overdue.
- **Builds clean** with `dotnet build`, **326 unit tests pass** with `dotnet test`.
- **Works end to end on real hardware.** The owner's own mic test: 3.2 s of speech → typed in 215 ms
  with Parakeet. Overlay, tray, settings, model download, model deletion are all verified in the running
  app.
- **LLM cleanup is in**, off by default: `LlmTextCleaner` runs the regex cleaner, then optionally a
  Claude rewrite under a 2 s timeout, falling back to the regex text on anything that goes wrong. Unit
  tested; the live path was verified only as far as a rejected key (see "Gotchas" 9).
- **The installed build's taskbar icon is fixed** as of 0.2.8, after a long hunt. The answer is in
  "Gotchas" 20, and it is not what anyone would guess.
- **The review in `docs/REVIEW-2026-09-11.md` is partly addressed**, findings 1–5 and 7 — transcript
  logging, history retention and deletion, hotkey suppression and parsing, delivery ordering, session
  lifetime, focus revalidation. That doc carries a status note listing what is left; **finding 6
  (clipboard restore) and finding 10 (the filler regex eating "um" in German and "ER" in English) are
  both still open**, and 6 matters more now that multiline results always paste.
- **Personalisation landed locally**: `PhraseBook` (spellings, replacements, snippets), Esc to cancel,
  an optional toggle key, optional sounds, and a recording limit.

## Repo map

```
Talk2Me.sln
src/Talk2Me.Core            pipeline state machine, interfaces, settings, regex cleaner, and the pure
                            reducers that make the awkward parts testable: Hotkey, HotkeyGesture,
                            Activation, Delivery, PhraseBook, NumberField, WindowPlacement  (no Windows deps)
src/Talk2Me.Windows         WH_KEYBOARD_LL hook, WaveIn mic capture, SendInput + clipboard injection
src/Talk2Me.Transcription   ParakeetTranscriber (sherpa-onnx), WhisperTranscriber (Whisper.net),
                            TranscriberRouter, model downloaders, ModelStorage
src/Talk2Me.Llm             ClaudeLlmClient — the only project that references the Anthropic SDK
src/Talk2Me.App             WPF tray app (namespace Talk2Me.Desktop): App.xaml has the palette + mark
                            geometry; Views/ has OverlayWindow, SettingsWindow, HistoryWindow, BrandMark;
                            Themes/ has Light.xaml, Dark.xaml and the templated Controls.xaml;
                            Services/ has ModelMaintenance, ThemeManager and LegacyMigration;
                            Logging/ has the file logger
tools/Talk2Me.Bench         transcribes a WAV with one or both engines, prints latency side by side
tools/Talk2Me.Clean         runs a transcript through the LLM cleanup pass, prints the rewrite + latency
tools/Talk2Me.Focus         prints what the focus probe makes of whatever window is in front
tools/Talk2Me.Brand         renders talk2me.ico + logo PNGs from the vector mark (WPF, no external tools)
tests/Talk2Me.Core.Tests    xUnit, 240 tests: DictationEngine + session lifetime, hotkey parsing and
                            gesture, activation (hold / toggle / cancel), PhraseBook, VocabularyFormat,
                            NumberField, Delivery, focus deflection, failed delivery, history store,
                            BasicTextCleaner, EngineSelection, LlmTextCleaner, CleanupPrompt,
                            ModelStorage, WindowPlacement, languages, stats, settings cloning
branding/                   BRAND.md, mark.svg, icon.svg, logo.svg, exports/
docs/                       ARCHITECTURE.md, HANDOFF.md
```

## Run, build, test

```bash
dotnet run --project src/Talk2Me.App          # tray app; first run downloads the active engine's model
dotnet test                                   # 326 tests, ~2 s
dotnet run --project tools/Talk2Me.Clean -- "um the deadline is monday no wait tuesday"
dotnet run --project tools/Talk2Me.Bench -- speech.wav Both 5
dotnet run --project tools/Talk2Me.Brand      # regenerate icon + exports after brand changes
```

Dev launch flags: `--settings` opens Settings at start; `--history` opens the history window;
`--dictation-box` opens the recovery scratchpad; `--overlay-demo` cycles the overlay through every
state so it can be styled without dictating.

Requirements: Windows 10/11, .NET 8 SDK. GPU optional. No CUDA Toolkit, no Rust, no Python.

## Where things live at runtime

`%LOCALAPPDATA%\Talk2Me\`

> **The data folder moved.** It is `%LOCALAPPDATA%\Jupitor Studio\Talk2Me` now, not
> `%LOCALAPPDATA%\Talk2Me`. `LegacyMigration` brings forward both the old Murmur folder and the old
> Talk2Me one.

- `settings.json` — all user settings; saved from the Settings window, hot-reloaded by every consumer.
- `models\ggml-large-v3-turbo.bin` (1.6 GB) and `models\parakeet-tdt-0.6b-v3-int8\` (640 MB).
- `apikey.dat` — the Anthropic key for the cleanup pass, DPAPI-encrypted under the current user. Kept
  out of `settings.json`, which is plain text. `ANTHROPIC_API_KEY` is the fallback.
- `history.jsonl` — every dictation, one JSON object per line, trimmed to `History.MaxEntries` (200 by
  default) **on disk**, not merely in the view. **Plain text**: this is everything the user has ever
  dictated. `History.Enabled` turns it off.
- `logs\talk2me.log` — rolling 5 MB. Debug level. Every dictation logs how many characters, how
  many audio seconds and how many ms — **never the text**. That was false until `77adef3`; both
  transcribers logged the recognised words at Debug, so turning history off left a second plaintext
  archive of everything the user had said.

On first run the app moves the old `%LOCALAPPDATA%\Murmur` folder here, so nothing is re-downloaded.

## Decisions and why

| Decision | Reason |
|---|---|
| .NET 8 + WPF, not Tauri/Electron | Key-*up* detection needs a low-level hook; text injection needs SendInput; the machine already had .NET 8 + VS 2022 and no Rust. One process, ~40 MB idle. |
| Two engines behind one interface | Parakeet TDT 0.6B v3 for English + 24 European languages (better English WER, never hallucinates on silence, no GPU needed). Whisper large-v3-turbo for the other ~75 languages. `EngineSelection.Resolve` picks; setting `Engine` = Auto/Parakeet/Whisper overrides. |
| Parakeet via sherpa-onnx on **CPU** | The NuGet runtime is CPU-only on Windows. Fast enough: 931 ms for 13 s of audio. A CUDA build exists only as a manual download. |
| Whisper runtime order CUDA12 → Vulkan → CPU | No CUDA Toolkit installed, so Vulkan is what runs. Installing the toolkit flips to CUDA automatically. |
| H.NotifyIcon.Wpf pinned to **2.3.2** | 2.4.x dropped net8.0 and silently resolves to the .NET Framework asset, which fails XAML compile. |
| WaveIn at 16 kHz mono, not WASAPI | The driver resamples for free to exactly what both engines want. Swap for WASAPI only if latency or loopback becomes a need. |
| Settings is a nav rail + pages, not one form | It had grown past 1400px with the expanders open and was genuinely hard to read. Seven pages (General / Transcription / Activation / Appearance / Vocabulary / AI cleanup / History) modelled on WhisperTyping, which the owner asked for by screenshot. |
| Light default with a dark toggle, pill always dark | The owner picked light with a toggle. The pill is the exception on purpose: it floats over other applications, so it has to read against *their* content, not ours. Its brushes are `Overlay.*` in App.xaml and never swap. |
| Controls are fully templated | WPF's stock ComboBox/CheckBox/Button chrome comes from system colours and looks wrong in dark. Templating them is the only way both themes are right. |
| The pill rests on screen instead of hiding | It is the only feedback the user has, and it is useless if it is gone when they glance at it. Resting dimmed keeps it available without being loud. `Overlay.AlwaysVisible = false` restores hide-on-idle. |
| Remembered positions are checked against real monitors, in physical pixels | The virtual desktop is a bounding box with holes in it on a multi-monitor setup, so a bounding-box check restores windows onto dead space where they cannot be seen or dragged. `WindowPlacement` uses the actual monitor rectangles. Physical pixels because DIP conversion needs the target monitor's DPI, which you do not know until you are on it. |
| The focus probe is permissive, and runs at key-*down* | Only a confident negative diverts text to the clipboard; `Unknown` types as before, because accessibility data is patchy and refusing a field that would have worked is worse than the problem. Probing at key-down makes it free — the user is speaking — and captures focus as it was when they started. |
| The bar takes clicks but not focus | It has a toolbar and can be dragged, so it can no longer be click-through. `WS_EX_NOACTIVATE` alone does both: Windows delivers the clicks and never activates the window, so the caret stays in the user's editor. `WS_EX_TRANSPARENT` and `IsHitTestVisible=False` are gone. |
| The pill is raised with `SWP_NOACTIVATE`, never `Activate()` | Focus must stay in whatever the user clicked into, or the dictation lands in the wrong window — the exact failure the history window exists to recover from. `Topmost` alone is not enough because a later topmost window sits above it, hence the explicit re-raise when dictation starts. |
| The history list expands rows in place | A list plus a detail pane makes two scroll regions compete for a 560px window: the list collapsed to a sliver and clipped rows mid-line, and the capped detail boxes scrolled short text to a fragment. Expanding in place leaves one scroll region and no truncation at any length. |
| History is append-only JSONL, not a database | A dictation must never be lost or slowed by the log. The hot path is one `File.AppendAllText`, failures are swallowed (the text is already typed), and the file is only rewritten when trimming or clearing. A torn line is skipped at load. |
| Cleanup is regex **then** optionally Claude | The regex pass always runs and is the fallback, so dictation degrades rather than breaks when the network, the key, or the timeout fails. Rewrite quality is the whole point of the LLM step, so it gets the raw transcript, not the regex output. |
| `ILlmClient` seam, Claude first | Core stays free of any provider SDK; `Talk2Me.Llm` holds the Anthropic dependency. A local model (llama.cpp / ONNX) implements the same two-method interface without touching the pipeline. Claude first because rewrite quality is what makes the feature worth having. |
| Cleanup off by default, key in DPAPI | It is the only thing that leaves the machine, so it must be a deliberate choice. `settings.json` is plain text, so the key lives in `apikey.dat` encrypted under the Windows account instead. |
| Effort `low`, adaptive thinking, no retries | The call has ~2 s. Low effort keeps it inside that; retries only delay the fallback. Thinking stays adaptive because disabling it on Opus can leak reasoning markup, which here would be typed into the user's window. |
| The awkward input logic lives in pure Core reducers | Auto-repeat, a release with no press, the hotkey changing mid-hold, toggle-vs-hold, Esc only while dictating — every one of those was a real bug and none is reachable from a test with Win32 in the way. `HotkeyGesture` and `Activation` take events and return decisions, so all of it is covered. |
| Every dictation is a `Session` | Cancellation needs something to cancel. An id, a `CancellationTokenSource` and the task let Escape actually stop transcription, stop a late dictation stamping on the one that replaced it, and let shutdown drain instead of tearing up mid-write. |
| The words are announced before delivery | History used to be written after typing, so a dictation that failed to deliver was lost along with the exception — the exact case the history window exists for. `Recognised` fires first now. |
| The phrase book runs again after the rewrite | The model is deliberately given the raw transcript, so it has no idea what the user has corrected and cheerfully undoes it. Re-applying afterwards is the only ordering where both features work. |
| Snippets need the word "insert" | Left implicit, a signature or an address expands in the middle of an ordinary sentence. They also skip the LLM pass entirely: a model asked to tidy up a signature will do exactly that. |
| Vocabulary is a text box, and its own file | These lists are written in bursts, usually pasted from somewhere, and plain text can be selected, sorted, diffed and kept in a note — a grid of rows with add/remove buttons cannot. The export is separate from `settings.json` because it is the user's own work, not window positions. |
| A bad number says so instead of being dropped or clamped | Save used to ignore an unusable value silently: the box kept what was typed, the setting did not change, and the window closed looking like it had worked. Clamping would be worse, since a value the user never chose would be saved under their name. `NumberField` holds the range and the message; Save waits. |
| The recording limit finishes rather than cancels | A key left under a book should not record all afternoon, but throwing the audio away would punish the user for the accident. Whatever was said still arrives. |
| Sounds use `SystemSounds`, off by default | They respect whatever scheme the user has chosen, silence included, and they need no asset files. |
| The dictation box never opens itself | It appears after a failed delivery, which is exactly when the user is mid-sentence in something else. A window arriving over that would be a worse interruption than the failure, and it would take the focus the rest of the app works so hard never to touch. The bar reports it; the user opens it. |
| Send-it-back is refused for elevated targets | Windows discards synthetic input aimed at a more privileged process and says nothing either time, so a second attempt fails exactly as silently as the first. The box says the text has to be pasted by hand rather than offering a button that cannot work. |
| An edit writes only what was typed | What was heard is the evidence a correction is learned from. An edit that overwrote it would destroy the pair the vocabulary needs, and the pair is the whole point of remembering corrections from history. |
| Remember… proposes the words that changed, not the sentence | A replacement rule for a whole sentence only ever fires on that exact sentence again. `CorrectionGuess` trims what both versions agree on from each end; words rather than characters, because a character diff of "jupitor"/"Jupiter" proposes letters nobody can read or edit. |
| Deleting one entry does not ask; Clear still does | One entry is a small, obviously-scoped action and a dialog would be in the way of the tidying-up it exists for. Clear takes everything at once, so it keeps its confirmation. |
| Clean again reprocesses text, never audio | Re-transcribing would mean keeping every recording ever made. The result goes into the draft rather than to disk, so seeing what cleanup would say now is separate from accepting it. |
| The caret is read at delivery, not at key-down | It is the one thing that moves while a dictation is being transcribed, so the answer from key-down would be stale exactly when it mattered. That puts an accessibility call on the path of finished text, hence the 250 ms budget and an immediate "do not know" when it expires. |
| Only a capital Talk2Me added is ever undone | Lowercasing a continuation is the whole point, and lowercasing a name the recogniser produced would be a visible, unattributable corruption of the user's words. Comparing the raw transcript with the finished text says which of the two this is; nothing about the surrounding sentence can. |
| A mode can only narrow the Claude permission, never grant it | Picking how a dictation should read must not be the act that authorises text leaving the machine. `MayUseLlm` is checked alongside `Cleanup.UseLlm`, and there is a test for each direction. |
| A mode's vocabulary is added to the main one | A correction the user has taught Talk2Me should not stop applying because they picked a different mode. The mode's entries go first, so the more specific one wins where both name a phrase. |
| Cycling the mode does nothing mid-dictation | Changing how the words will be treated halfway through saying them is not something anyone means, and it would silently reinterpret a recording already in progress. |
| Switching mode saves | A mode nobody can see the state of after a restart is worse than one extra settings write. The bar carries its name for the same reason. |
| Brand assets rendered by a WPF tool | Same geometry as the in-app XAML, zero external dependencies, reproducible from `dotnet run`. |

## Measured numbers (owner's machine: i7-11700F, RTX 4060 Ti 8 GB)

Same 13 s clip, best of 5:

| Engine | Runs on | Load | Transcribe | Note |
|---|---|---|---|---|
| Parakeet int8 | CPU, 8 threads | 3.9 s | 931 ms | lower-cased one proper noun |
| Whisper large-v3-turbo | GPU (Vulkan) | 2.4 s | 413 ms | first-ever warm-up ~20 s (shader compile), then 0.4 s |

Both transcripts were otherwise identical and correctly punctuated.

## Gotchas the next person will hit

1. ~~Repo folder is still named `Murmur`.~~ Done — it is `~/source/repos/Talk2Me` now.
2. **Synthetic key presses do not trigger the hotkey.** The hook ignores `LLKHF_INJECTED` events on
   purpose (so our own SendInput cannot retrigger it). To test without a physical key use the tray item
   "Test dictation (records 3 s)" or `DictationEngine.BeginDictation()/EndDictation()`.
3. **The app cannot steal focus when started from a script**, so a scripted `--settings` launch may open
   behind other windows. It is there; use `PrintWindow` by handle if you need a screenshot.
4. **Tooling run from inside the Claude desktop app is MSIX-sandboxed.** Its `%LOCALAPPDATA%` writes go to
   `…\Packages\Claude_*\LocalCache\Local\`, not the real folder. Do not be surprised when the user's data
   folder and the tool's data folder differ.
5. **Rebuilding the app fails while it is running** (DLLs locked). Quit from the tray first.
6. **Whisper on a very short clip** is padded to 1.5 s in `WhisperTranscriber`; whisper.cpp rejects
   shorter input. Parakeet is padded to 0.5 s.
7. **Clipboard paste mode restores only text.** If the user had an image on the clipboard when a long
   dictation pasted, it is gone. Documented in `ClipboardPasteInjector`.
8. **Parakeet is CC-BY-4.0.** Attribution to NVIDIA belongs in the eventual About screen. Whisper is MIT.
9. **The cleanup pass has never made a successful API call.** No key was available in the session that
   built it. It was verified as far as the API rejecting an invalid key in ~600 ms and the fallback
   typing the regex text — so the request shape is accepted and the failure path works, but nobody has
   seen a real rewrite or its latency yet. Run `tools/Talk2Me.Clean` with a real key first thing.
10. **`ModelStorage.Delete` only ever removes something `List()` reported**, so a caller cannot compose
    a path out of the models folder. Keep that property if you add another delete path.
11. **`Talk2MeSettings.Clone` is no longer a plain `MemberwiseClone`.** `Cleanup`, `History`, `Overlay` and `Appearance` are
    nested objects, deep-copied by hand. Any future nested settings section needs the same treatment or
    the Settings window will edit live settings in place.
12. **The history window saves settings when it moves or closes**, via clone-modify-save on
    `SettingsStore`. If the Settings window is open with unsaved edits at that moment, last writer wins.
    Not worth solving until someone actually hits it, but it is why the two can disagree.
13. **Closing the history window hides it**; only `AllowClose` (set on app exit) really closes it. If you
    add another way to shut the app down, set that flag or the window will block it.
14. **Never call `Activate()`, `Focus()` or `SetForegroundWindow` on `OverlayWindow`.** It would take
    focus from the window the user is dictating into. Raise it with `SetWindowPos` + `SWP_NOACTIVATE`;
    `RaiseWithoutActivating()` is there for exactly this.
15. **A resource key is a brush or a style, never both.** `Ui.Card` (brush) and the old `Ui.Card` (Border
    style) collided and every window died on open with *'System.Windows.Style' is not a valid value for
    property 'Background'*. The style is `Ui.CardSurface` now.
16. **Never put a fixed `Height` on the templated TextBox/PasswordBox styles.** It starves
    `PART_ContentHost` and the text stops rendering while the control still reports its value through UI
    Automation — it looks like a broken binding and is not. Use `MinHeight`.
17. **Light.xaml and Dark.xaml must define the same keys.** A key missing from one only fails once a
    user switches to that theme.
18. **The status bar has two different "go away" buttons, and neither quits anything.** Close (`✕`)
    sets `Overlay.AlwaysVisible = false` — it stops resting on screen but still appears while
    dictating, and is persisted. Minimise (`─`) sets `OverlayViewModel.IsHidden` — gone entirely,
    including during dictation, and deliberately *not* persisted, so a restart brings it back. While
    minimised the only ways back are the tray icon (left click, or "Show status bar" on the menu) and
    turning Appearance → "Keep the pill on screen" from off to on.
19. **Never let the installer's packId be `Talk2Me`.** Velopack installs to `%LOCALAPPDATA%\<packId>`
    and *clears that folder first*. Builds before the installer kept user data in
    `%LOCALAPPDATA%\Talk2Me`, so packing with that id destroys settings, history and gigabytes of
    downloaded models before the app can migrate them. It happened once during development. The packId
    is `Talk2MeApp` and the data folder is `%LOCALAPPDATA%\Jupitor Studio\Talk2Me`; both halves of
    that separation matter.
20. **Velopack's init takes over the taskbar button's icon.** `VelopackApp.Build().Run()` sets a
    process-wide AppUserModelID, and from then on Windows resolves the button's icon through that
    identity rather than `Window.Icon`, falling back to a generic one. Everything else still looks
    right — the exe's icon, the Start Menu shortcut, the hover thumbnail — which makes it read as an
    icon-cache problem. It is not.

    Fixed, but only by getting **both** of these right at once, which is why it took so long:
    - The window needs `PKEY_AppUserModel_RelaunchIconResource` **and** a
      `PKEY_AppUserModel_ID` of its own. Neither alone does anything.
    - That ID must be one **no installed shortcut claims** (`JupitorStudio.Talk2Me`). Set it to
      Velopack's own `velopack.Talk2MeApp` — which is what matching the process ID gives you, and what
      looks obviously correct — and the shell serves the icon registered for that app instead, i.e.
      the generic one. It ignores the property entirely.
    - `TaskbarWindow.StageIcon` copies the icon to `%TEMP%\Talk2Me\taskbar.ico` at every start and
      points the property there rather than at the executable. **That was justified by a measurement
      that turned out to be an artefact** — see gotcha 28 — and pointing the property straight at the
      installed executable may well work. It stays as it is pending a re-test against an install the
      user performed themselves; staging costs nothing and works either way.

    Also measured, so nobody re-derives it:
    - Cause confirmed by isolation: a build skipping only the Velopack call shows the correct icon.
    - `binary,-resourceId` (e.g. `Talk2Me.exe,-32512`) and `icon.ico,0` are both accepted forms. The
      `exe,0` index form that works in a shortcut resolves to nothing here.
    - `WM_SETICON` with `ExtractIconEx`, restarting Explorer, and a fresh uninstall/reinstall all
      change nothing. Neither does an unregistered *process* AUMID with no window property.
    - **How to iterate on this without packing an installer.** The whole difference between a plain
      build and an installed one is the process AUMID, so `TALK2ME_TEST_AUMID` makes `App` claim one at
      startup and reproduces the bug from `dotnet build`. `TALK2ME_TEST_ICONRES`,
      `TALK2ME_TEST_WINDOWID` and `TALK2ME_TEST_SKIPPROP` then vary the icon, the identity, or skip the
      property, so a trial is a rebuild and a screenshot rather than a pack, uninstall and install.
21. **`TaskbarWindow` must keep a normal window style.** It looks like it wants
    `WindowStyle="None"` + `AllowsTransparency` since it is never meant to be seen, but that stops WPF
    applying `Window.Icon` and the taskbar button falls back to a generic Windows icon. Being 1x1 at
    -32000 and always minimised is what keeps it invisible.
22. **Do not re-add `WS_EX_TRANSPARENT` to `OverlayWindow`.** It would make the toolbar unclickable and
    dragging impossible. `WS_EX_NOACTIVATE` is what keeps focus where it belongs.
23. **Never check a remembered window position against `SystemParameters.VirtualScreen*`.** That is the
    bounding box of every monitor, and on the owner's four-monitor layout it contains regions no monitor
    covers. Use `WindowPlacement.Restore` with `MonitorLayout.WorkAreas()`.
24. **`IsCancel="True"` does nothing on a modeless window.** WPF's cancel handling sets `DialogResult`,
    which only applies to a window shown with `ShowDialog`. Settings is shown with `Show`, so its Cancel
    button needs a real `Click` handler and Escape needs wiring by hand — via bubbling `OnKeyDown`, not
    `OnPreviewKeyDown`, so an open combo box dropdown still gets Escape first.
25. **`Talk2Me.Windows` sets `UseWPF` only for the UI Automation client assemblies.** It draws no UI.
    Turning it on also changed the implicit usings, which is why `DpapiApiKeyStore` now imports
    `System.IO` explicitly.
26. **Chromium reports its page body as a read-only Document and a focused input as an Edit.** That is
    the discrimination the probe relies on, and it was verified against real Chrome — check it again if
    the control-type rules are ever touched, because getting it wrong breaks dictation into web forms.
27. **`Overlay.WindowLeft/Top` and `History.WindowLeft/Top` are physical pixels, not WPF units.**
    `History.WindowWidth/Height` are still WPF units. Values saved before this change were WPF units;
    they differ only under DPI scaling, and a wrong one is clamped on the next launch rather than lost.
28. **Nothing an agent session installs is real.** Claude Code's sandbox redirects writes under
    `%LOCALAPPDATA%`, `%APPDATA%` and `HKCU` into a per-session overlay, and serves reads back out of
    it — including under `dangerouslyDisableSandbox`, which does *not* escape the redirection. So an
    installer run from inside a session produces a Talk2Me that looks perfectly installed from in
    there and does not exist at all from outside: no install directory, no data folder, no `Uninstall`
    registry entry, and so no row in Programs and Features or in Settings. `%TEMP%`, `C:\` and the
    repo are not redirected, which is the only reason anything written there behaved normally.

    Two things follow, and both cost hours to learn:
    - **Only the user can install it**, and verifying the result needs a process the session did not
      start. Have the Task Scheduler spawn one: write a `.cmd`, then `schtasks /create /tn X /tr
      <file> /sc once /st 23:59 /f`, `schtasks /run /tn X`, read the file it leaves behind. That
      process sees the real machine.
    - This is what made `%LOCALAPPDATA%` look like a tree the shell refused to read icons out of
      (gotcha 20). The files were simply not there for Explorer.

29. **`Hotkey` is a record whose modifier list is compared by reference unless you stop it.** Two
    identical hotkeys compared unequal, so "has the hotkey changed?" was always true and the hook
    re-registered on every settings save. `Equals`/`GetHashCode` are written out by hand; keep them if
    you add a field.
30. **No key name may contain `+`.** The parser splits on it, so "Numpad +" parsed as "Numpad", failed,
    and the hotkey silently reverted to Right Ctrl. The numpad operators are spelled out — Numpad Plus,
    Minus, Multiply, Divide, Dot — with the old names kept as aliases, and a test walks every key this
    can name asserting no name contains the separator and each survives a round trip.
31. **Escape has to reset the toggle gesture, not just the flag.** Cancelling while a toggle dictation
    was running left the gesture believing it was still on, so the next press *stopped* a dictation
    that had never started. Found by a test, which is the only reason it was found at all.
32. **Raise state changes outside the lock.** `DictationEngine` deadlocked against handlers that called
    back into it. The state is computed under the lock and the event raised after it is released.
33. **The multiline rule lives in `Delivery`, not the injector.** Text with a line break always pastes,
    whatever the injection mode says, because typing it sends Return — which submits the chat message,
    triggers the form, or runs the command. Mode "Type" cannot be allowed to mean that.
34. **`dotnet build` fails while the app is running, and the error names the lock but not the cause.**
    Quit from the tray first (gotcha 5). To verify a build without disturbing a running instance, build
    to a different output directory with `-o`; the compile is what you are checking, and the copy step
    is the only thing that fails.
35. **Only one Talk2Me can run per session** — a `Local\Talk2Me.SingleInstance` mutex. A second copy
    exits silently, so a scripted launch aimed at testing a new build will quietly drive the *old* one
    that is already up. Check for a running process before believing a screenshot.

36. **`SetForegroundWindow` returns before the switch has happened.** Typing immediately after it
    loses the first characters into whatever was still in front. `Win32WindowActivator` polls
    `GetForegroundWindow` until it matches, and gives up after 600 ms. Also: a process can only *give
    away* the foreground, never take it, so this works from a button in a window that is already in
    front and cannot be made to work from the background.
37. **Merging `App.xaml` into another `Application` throws.** Loading it constructs `Talk2Me.Desktop.App`,
    and WPF allows one `Application` per AppDomain — so a test harness that wants the app's windows has
    to supply the brand keys itself rather than merging the dictionary that defines them. Window
    `Icon` pack URIs resolve against the *entry* assembly too, so the harness needs its own copy of
    `talk2me.ico` as a `Resource`.

38. **An owned WPF dialog is a UIA *descendant* of its owner, not a child of the root.** A
    `ShowDialog` with `Owner` set does not appear in `RootElement`'s children, so a script looking for
    it there concludes it never opened. Search the owner's descendants, or the root's. This wasted a
    debugging round trip on a dialog that had been working the whole time.

39. **`TextPattern` and `ValuePattern` are not the same answer.** Plenty of ordinary edit controls
    expose `ValuePattern` only: they can say what they hold but not where the caret is in it, which is
    useless for fitting text around an insertion point. `ReadCaret` requires `TextPattern` and returns
    Unknown otherwise. `TextPatternRangeEndpoint` and `TextUnit` live in
    `System.Windows.Automation.Text`, not `System.Windows.Automation`.
40. **Do not send synthetic keystrokes to verify anything.** A scripted `SendKeys` goes to whatever has
    focus, which is the user's window, not yours — it typed test text into an application during this
    session. Use a harness window you own, and bring it to the front from the script with
    `SetForegroundWindow` on its `MainWindowHandle`, since a scripted launch opens behind (gotcha 3).

41. **A bare modifier is its own trigger.** `Hotkey("Right Ctrl")` has the key in `Modifiers` *and*
    as `Trigger`, so a test helper that presses every modifier and then the trigger sends it twice —
    and the second one reads as auto-repeat, not a press. Press `Required` then `Trigger`.
42. **UI Automation reads an item's `ToString()`, not its `DisplayMemberPath`.** A combo box showed
    the right names on screen while exposing `Talk2Me.Core.Settings.DictationMode` to every screen
    reader and every test script. `DictationMode` overrides `ToString()`; do the same for anything
    else that ends up in a list.

## Roadmap, in the order I would do it

1. **Close review findings 6 and 10.** Finding 6 is the clipboard: the restore races the paste, and only
   text is put back, so an image or formatted content on the clipboard is destroyed by a dictation. That
   got more likely, not less, when multiline results started always pasting. Finding 10 is the filler
   regex — it deletes German "um" and English "ER", so *"The ER is open"* becomes *"The is open"*. Both
   are small, both are user-visible, and both are already written up with reproductions.
2. **Prove the rewrite on real dictation** and tune the prompt in `CleanupPrompt` against it. Still true:
   nobody has seen a successful call (gotcha 9).
3. **Cut a release.** The latest is `v0.2.9`; everything since — the review fixes, the phrase book,
   cancel and toggle, the settings validation — is in no installed copy. Nothing here is in a user's
   hands yet.
4. **Bring Remember… to the dictation box** as well. It is in the history window now; the box is the
   other place the wrong words are already on screen.
5. **Per-app modes**: read the foreground window's process name at release time and pick a mode from
   it. `FocusTarget.ProcessName` is already captured at key-down, so this is a map from process name
   to mode name and nothing else. Feature research §5 calls it out as the "later" half of modes.
6. **Streaming partials** while the key is held (Parakeet is a transducer; it suits this).
7. **Overlay polish**: replace the level bar with an animated waveform; onboarding window on first run.
8. **Command mode**: hold a second key, speak an instruction, replace the selected text.
9. **Local LLM backend** behind `ILlmClient`, so the rewrite works offline and the "nothing leaves this
   machine" promise holds with cleanup switched on.
10. **Auto-start with Windows** and crash recovery. The installer and the single-instance guard are done.

## Session log (what was actually done, in order)

1. Researched Wispr Flow and engine options; profiled the machine; chose .NET 8 + WPF.
2. Scaffolded five projects; wrote Core pipeline + tests; Windows hook/audio/injection; Whisper.net.
3. Built the WPF app: tray, overlay, settings, file logging. Verified with screenshots from the running app.
4. Added Parakeet via sherpa-onnx, the engine router and selection tests, and a two-engine benchmark.
5. Added model deletion (Settings + tray) with engine unload first. Committed.
6. Renamed everything to Talk2Me; designed the mark, palette, and wordmark; built the brand render tool;
   restyled the overlay and settings; added the legacy data migration. Committed.
7. Added the LLM cleanup pass: `ILlmClient` + `IApiKeyStore` in Core, `LlmTextCleaner` with its timeout
   and fallbacks, `CleanupPrompt`, the `Talk2Me.Llm` project with `ClaudeLlmClient`, DPAPI key storage,
   a `Polishing` pipeline state, the Settings expander, `tools/Talk2Me.Clean`, and 20 more tests.
8. Made model deletion selective: `ModelStorage.Delete(name)` per entry, `ModelMaintenance.List()` with
   friendly labels and an "in use" flag, a tick list in Settings with **Delete selected** / **Delete
   all**. The tray item still deletes everything.
9. Added the dictation history: `DictationRecord` + `IDictationHistory` in Core,
   `DictationHistoryStore` (JSONL), `HistorySettings`, the always-on-top `HistoryWindow` with
   "Copy last dictation" and per-entry copy, a tray item, a Settings section, and 9 more tests.
10. Made the pill permanent: `OverlaySettings` (always-visible, position, resting opacity, margin), a
    resting state in `OverlayViewModel` instead of hiding, `RaiseWithoutActivating()` so becoming active
    re-asserts z-order without touching focus, and Settings controls for it.
11. Redesigned Settings as a nav rail plus six pages with stats tiles, added the light/dark/system theme
    system (`Themes/`, `ThemeManager`, `AppearanceSettings`), themed the history window, and pinned the
    pill's colours so it stays dark. Verified every page in both themes by driving the running app.
12. Rebuilt the history list to expand rows in place after the master-detail layout proved unreadable.
13. Turned the pill into a 433px bar with a toolbar (Settings, History, Copy last, collapse, hide), made
    it draggable with the position persisted, and gave listening a live waveform and an elapsed timer.
    Verified with synthetic mouse input that clicking and dragging it leaves the foreground window
    untouched.
14. Fixed remembered window positions on multi-monitor setups: `WindowPlacement` + `MonitorLayout`
    replace the virtual-desktop bounding-box check, positions moved to physical pixels, and both
    windows now react to `DisplaySettingsChanged`.
15. Turned the pill's minimise button into a real hide, with tray and taskbar routes back.
16. Added the focus probe: dictation now checks whether the focused element can take text (and whether
    the target is elevated) and falls back to the clipboard with "Copied instead" when it cannot.
17. Packaged with Velopack and fixed the installed build's generic taskbar button icon — the long hunt
    written up in gotcha 20, and the sandbox discovery in gotcha 28 that explained why so much of it
    looked impossible.
18. Polished the settings window: dark title bars and scroll bars, a language picker by name, engine-
    dependent fields greyed rather than lying, opt-in model downloads, and the version taken from the
    release tag instead of a literal.
19. Acted on the owner's own review (`docs/REVIEW-2026-09-11.md`): stopped transcripts reaching the log,
    made history retention and deletion mean the file, rewrote hotkey suppression and parsing as pure
    reducers, made multiline text always paste, moved history ahead of delivery, gave each dictation a
    session with cancellation and a shutdown drain, and re-checked focus at delivery time.
20. Acted on `docs/FEATURE-RESEARCH-2026-09-11.md`: Esc to cancel, an optional toggle key, the local
    phrase book with snippets and an import/export file, optional sounds, and a recording limit.
21. Gave every numeric settings box real validation, after noticing the two new settings had shipped
    with no control at all and the existing boxes dropped bad values in silence.
22. Built the dictation box (feature research §4): `Recovery` in Core decides what can honestly be
    offered for a dictation that did not arrive, and the window holds the text, editable, until the
    user is done with it — copy it, correct it, or hand the foreground back to the window it was aimed
    at and type it there.
23. Turned history into a correction tool (feature research §7): search across what was typed and what
    was heard, edit a past dictation, re-run cleanup over the raw transcript, delete single entries,
    and save a vocabulary replacement from a mistake — with `CorrectionGuess` proposing the words that
    actually changed rather than the whole sentence.
24. Made insertion aware of the caret (feature research §6): `ReadCaret` through `TextPattern` at
    delivery time, and `CaretFit` deciding spacing and the first letter's case from what is actually
    either side of the insertion point.
25. Added dictation modes (feature research §5), entirely locally: `DictationMode` bundles the
    post-processing choices, four ship built in, a key cycles them, the bar shows which is in charge,
    and a mode can only ever narrow the permission to use Claude — never grant it.

## Links

- Parakeet model: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 (sherpa-onnx int8 export:
  https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8)
- Whisper.net: https://github.com/sandrohanea/whisper.net
- sherpa-onnx: https://github.com/k2-fsa/sherpa-onnx
- Reference product: https://wisprflow.ai
