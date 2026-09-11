# TawkType — handoff

Written 2026-09-10 at the end of the first build session; updated through 2026-09-11 after the
review, the feature passes, first run, the licence and the uninstall work. Read this first; then `README.md` for usage, `docs/ARCHITECTURE.md` for design,
`branding/BRAND.md` for the identity. `docs/REVIEW-2026-09-11.md` and
`docs/FEATURE-RESEARCH-2026-09-11.md` are the two assessments that drove most of what follows.

## What this is

**TawkType — you talk, it types.** Local voice typing for Windows, modelled on Wispr Flow. Hold Right
Ctrl, speak, release; the text is typed into whatever has focus. Speech recognition runs on the
machine. There is no account and no telemetry, and the only thing that ever leaves the computer is the
optional Claude rewrite, which is off until someone turns it on and supplies a key.

Owner: Jupitor Studio. Home: [tawktype.com](https://tawktype.com). Working name was **Murmur**, then
a second name that turned out to be another dictation product's; it is now **TawkType**. That older
name is a trademark belonging to somebody else, so it is not recorded anywhere in this repository —
not in a string, an identifier, an environment variable or a comment. See "The rename" below.

Windows only.

## State of the code

- **Branch `main`, clean tree, pushed.** `v0.5.0` is the only release and the only tag — everything
  earlier was deleted, releases and tags alike, because nothing had ever been installed from them.
  **Main is four commits ahead of that tag**: first run, the name sweep, the licence, and the
  uninstall data question. A release cut from `main` today would be the first to contain any of them.
- **Nothing here has ever been run outside a developer checkout.** That, not the release, is what is
  overdue, and three separate things now depend on it: the taskbar icon (gotcha 20), the first run's
  practice dictation, and the uninstall hook. See the roadmap.
- **Builds clean** with `dotnet build`, **385 unit tests pass** with `dotnet test` in about two seconds.
- **Works end to end on real hardware.** The owner's own mic test: 3.2 s of speech → typed in 215 ms
  with Parakeet. Overlay, tray, settings, model download and deletion are verified in the running app.
- **Renamed to TawkType, completely.** Name, mark, palette, namespaces, projects, solution, assembly,
  window identity, installer, data folder. Nothing in the repository carries the old name, and the
  migration code that used to carry old data forward has been deleted — nothing was ever installed under the
  old name, so there was nothing to carry. See "The rename" below.
- **It has a licence now, and it is not an open-source one.** `LICENSE.md`: use the application for
  anything, free; read and build the source; no redistribution and no derivative works. Source
  available, not open source — do not describe it as open source anywhere.
- **The install folder and the data folder differ by three letters.** The app installs to
  `%LOCALAPPDATA%\TawkTypeApp`; data lives in `%LOCALAPPDATA%\TawkType`. Velopack clears its own folder
  on every install, so those two must never converge — gotcha 19, and `DataFolderTests` reads the id
  out of `build/pack.ps1` to enforce it.
- **The whole pipeline is session-scoped**: a dictation can be cancelled with Esc, cannot be stamped
  on by a later one, and shutdown drains rather than tearing up mid-write.
- **Post-processing is local first.** `PhraseBook` applies the user's spellings, replacements and
  snippets with no model involved, and again after a rewrite so Claude cannot undo them. Four
  **modes** bundle the post-processing choices and a key cycles them; a mode can only ever *narrow*
  the permission to use Claude, never grant it.
- **Delivery knows where it is going.** The focus probe says whether text can land and re-checks at
  delivery time; `CaretFit` reads the words either side of the caret and fixes the spacing and the
  first letter's case. Anything unreadable behaves exactly as it did before either existed.
- **Nothing is lost when delivery fails.** The words are announced before they are typed, so they
  reach the history either way, and the **dictation box** holds them — editable, copyable, and able to
  hand the foreground back to the window they were aimed at.
- **History is a correction tool**, not just a log: search across what was typed and what was heard,
  edit, re-run cleanup, delete one entry, and save a vocabulary replacement from a mistake.
- **LLM cleanup is in**, off by default. Unit tested; the live path was verified only as far as a
  rejected key (see "Gotchas" 9) — **nobody has yet seen a real rewrite**.
- **The installed build's taskbar icon is fixed** as of 0.2.8, after a long hunt. The answer is in
  "Gotchas" 20, and it is not what anyone would guess.
- **The review in `docs/REVIEW-2026-09-11.md` is mostly addressed.** Findings 1–5, 7 and 8 are closed;
  10, 11 and 14 are partly closed. **Finding 6, the clipboard, is the largest still fully open**, and
  it matters more now that multiline results always paste. That doc carries a status table.
- **`docs/FEATURE-RESEARCH-2026-09-11.md` §1–§8 are built**, except §5's per-application defaults,
  which the section itself puts later. §9 (visible privacy during use) is untouched.
- **There is a first run** (§8): seven steps ending in a real dictation into a box TawkType owns. It
  saves each answer as it is given, because the later steps use them, and no step is satisfied by the
  user agreeing to it — the microphone gate wants a level, the model gate wants an engine that
  *loaded*, and the last gate wants words back from the pipeline. **The practice dictation is the one
  part nobody has watched work**: an agent session has no voice. Everything else in the flow was
  driven and screenshotted, including the download and the warm-up.
- **Uninstalling can take the data with it, if the user says so in advance.** A setting on
  Settings → General, obeyed silently by Velopack's uninstall hook — which may show no UI and is
  killed after 30 seconds, so the question cannot be asked during the uninstall itself. A
  *Delete my data…* button beside it does the same thing on demand. `DataRemoval.Check` guards both.
  The hook also clears the start-with-Windows entry every time, data question or not: TawkType writes
  that value itself, so nothing else would. **The hook has never executed.**

## Repo map

The project and namespace names still say TawkType. That is deliberate — see "The rename" below.

```
TawkType.sln
src/TawkType.Core            pipeline state machine, interfaces, settings, regex cleaner, and the pure
                            reducers that make the awkward parts testable, all free of Windows deps:
                              Input/     Hotkey, HotkeyGesture, Activation (hold / toggle / Esc / mode)
                              Text/      PhraseBook, CaretFit, CorrectionGuess, Delivery, BasicTextCleaner
                              Settings/  DictationMode, VocabularyEdit, VocabularyFormat, NumberField,
                                         DataRemoval (the guard in front of every recursive delete)
                              History/   DictationHistoryStore, HistoryQuery, LastDictation
                              Pipeline/  DictationEngine, Recovery
                              Onboarding/ SetupPlan (the steps and their gates), HotkeyCheck,
                                         MicrophoneCheck
src/TawkType.Windows         WH_KEYBOARD_LL hook, WaveIn mic capture, SendInput + clipboard injection,
                            UiaFocusProbe (can text land here, and what is either side of the caret),
                            Win32WindowActivator, DPAPI key store, Run-key startup, taskbar identity
src/TawkType.Transcription   ParakeetTranscriber (sherpa-onnx), WhisperTranscriber (Whisper.net),
                            TranscriberRouter, model downloaders, ModelStorage
src/TawkType.Llm             ClaudeLlmClient — the only project that references the Anthropic SDK
src/TawkType.App             WPF tray app (namespace TawkType.Desktop): App.xaml has the palette + mark
                            geometry; Views/ has OverlayWindow, SettingsWindow, SetupWindow,
                            HistoryWindow, DictationBoxWindow, RememberWindow, TaskbarWindow,
                            HotkeyBox, BrandMark;
                            Themes/ has Light.xaml, Dark.xaml and the templated Controls.xaml;
                            Services/ has ModelMaintenance, AppDataMaintenance, ThemeManager,
                            UpdateService, SoundCues,
                            Logging/ has the file logger
tools/TawkType.Bench         transcribes a WAV with one or both engines, prints latency side by side
tools/TawkType.Clean         runs a transcript through the LLM cleanup pass, prints the rewrite + latency
tools/TawkType.Focus         what the focus probe makes of the front window, and the text around its caret
tools/TawkType.Brand         renders tawktype.ico + logo PNGs from the vector mark (WPF, no external tools)
tests/TawkType.Core.Tests    xUnit, 385 tests. One file per behaviour; the names are the specification.
branding/                   BRAND.md, mark.svg, icon.svg, logo.svg, exports/
docs/                       ARCHITECTURE.md, HANDOFF.md, the two dated assessments, images/,
                            tawktype-brand/ (the design package; untracked, see .gitignore)
```

## Run, build, test

```bash
dotnet run --project src/TawkType.App          # tray app; first run downloads the active engine's model
dotnet test                                   # 385 tests, ~2 s
dotnet run --project tools/TawkType.Clean -- "um the deadline is monday no wait tuesday"
dotnet run --project tools/TawkType.Bench -- speech.wav Both 5
dotnet run --project tools/TawkType.Brand      # regenerate icon + exports after brand changes
```

Dev launch flags: `--settings` opens Settings at start; `--history` opens the history window;
`--dictation-box` opens the recovery scratchpad; `--setup` opens the first-run walkthrough (it opens
by itself when `settings.json` is absent); `--overlay-demo` cycles the overlay through every state so
it can be styled without dictating.

Requirements: Windows 10/11, .NET 8 SDK. GPU optional. No CUDA Toolkit, no Rust, no Python.

## Where things live at runtime

`%LOCALAPPDATA%\TawkType\`

> **Data lives in `%LOCALAPPDATA%\TawkType`**, and the application installs to
> `%LOCALAPPDATA%\TawkTypeApp`. Those two must stay different: Velopack clears the install folder on
> every update. There is no migration from the older locations any more — nothing was ever installed
> from a release, so there was nothing to carry forward.

- `settings.json` — all user settings; saved from the Settings window, hot-reloaded by every consumer.
- `models\ggml-large-v3-turbo.bin` (1.5 GB) and `models\parakeet-tdt-0.6b-v3-int8\` (640 MB).
  **Neither is downloaded automatically** — Settings → Transcription does it, on the user's say-so.
- `apikey.dat` — the Anthropic key for the cleanup pass, DPAPI-encrypted under the current user. Kept
  out of `settings.json`, which is plain text. `ANTHROPIC_API_KEY` is the fallback.
- `history.jsonl` — every dictation, one JSON object per line, trimmed to `History.MaxEntries` (200 by
  default) **on disk**, not merely in the view. **Plain text**: this is everything the user has ever
  dictated. `History.Enabled` turns it off.
**Uninstalling leaves all of it alone**, unless the user ticked *Delete all of this if I uninstall
TawkType* on Settings → General. Velopack's uninstall hook reads that one setting and deletes the
folder silently; everything else about the question is asked in the application, because the hook is
not allowed to ask anything. `docs/ARCHITECTURE.md` has the shape of it.

- `logs\tawktype.log` — rolling 5 MB. Debug level. Every dictation logs how many characters, how
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
| Whisper runtime order Vulkan → CPU | The CUDA 12 backend is a 538 MB DLL, several times the size of the rest of the app, and does nothing without the CUDA Toolkit installed. It is not shipped. Vulkan works with the stock NVIDIA/AMD/Intel driver. |
| H.NotifyIcon.Wpf pinned to **2.3.2** | 2.4.x dropped net8.0 and silently resolves to the .NET Framework asset, which fails XAML compile. |
| WaveIn at 16 kHz mono, not WASAPI | The driver resamples for free to exactly what both engines want. Swap for WASAPI only if latency or loopback becomes a need. |
| Settings is a nav rail + pages, not one form | It had grown past 1400px with the expanders open and was genuinely hard to read. Eight pages (General / Transcription / Activation / Modes / Appearance / Vocabulary / AI cleanup / History) modelled on WhisperTyping, which the owner asked for by screenshot. |
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
| `ILlmClient` seam, Claude first | Core stays free of any provider SDK; `TawkType.Llm` holds the Anthropic dependency. A local model (llama.cpp / ONNX) implements the same two-method interface without touching the pipeline. Claude first because rewrite quality is what makes the feature worth having. |
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
| Only a capital TawkType added is ever undone | Lowercasing a continuation is the whole point, and lowercasing a name the recogniser produced would be a visible, unattributable corruption of the user's words. Comparing the raw transcript with the finished text says which of the two this is; nothing about the surrounding sentence can. |
| A mode can only narrow the Claude permission, never grant it | Picking how a dictation should read must not be the act that authorises text leaving the machine. `MayUseLlm` is checked alongside `Cleanup.UseLlm`, and there is a test for each direction. |
| A mode's vocabulary is added to the main one | A correction the user has taught TawkType should not stop applying because they picked a different mode. The mode's entries go first, so the more specific one wins where both name a phrase. |
| Cycling the mode does nothing mid-dictation | Changing how the words will be treated halfway through saying them is not something anyone means, and it would silently reinterpret a recording already in progress. |
| Switching mode saves | A mode nobody can see the state of after a restart is worse than one extra settings write. The bar carries its name for the same reason. |
| The uninstall question is asked in the app, not during the uninstall | Velopack's hooks "may not show any UI" and are killed after 30 seconds. A prompt there would be against the contract and would hang on anyone who walked away mid-answer. So Settings carries the choice and the hook only obeys it. |
| Uninstalling keeps the data by default | Up to 1.5 GB of models, the vocabulary, and every dictation ever made. Throwing that away on an assumption is worse than leaving a folder behind, and a reinstall then picks up where the user left off. |
| A pure guard in front of every recursive delete | `DataRemoval.Check` refuses anything near the root of a drive, anything relative, the install folder, and any folder containing it. The data folder and the install folder differ by three letters (gotcha 19) and one caller runs during an uninstall with nobody watching, so the check is a tested function rather than an `if`. |
| Brand assets rendered by a WPF tool | Same geometry as the in-app XAML, zero external dependencies, reproducible from `dotnet run`. |
| Source available, not open source | The source is published so the privacy claim can be checked rather than believed — that is most of the argument for a local dictation app. It is not published so somebody can ship a fork. Use is unrestricted and free, including commercially; distribution and derivative works are not granted. |
| A bespoke licence rather than an off-the-shelf one | There is no well-known licence for "any use, no derivatives". PolyForm Strict looks like the fit and is **noncommercial** — its permitted purposes are personal use and noncommercial organisations. Noncommercial, Shield and Small-Business all permit derivative works; Internal-Use excludes personal use. Check the text before repeating any claim about which licence does what. |
| The licence is reachable from inside the app | Settings → General → About has a Licence button. Most people who run TawkType will never see the repository, and a proprietary licence nobody can find is not much of one. |
| First run saves each answer as it is given | The steps after it use them for real: the meter opens the device just chosen, the engine loads that language's model, the hook binds that key. A draft held back until Finish would have the user practise against the old settings — so there is no Cancel button, because there is nothing to roll back. |
| No step is satisfied by the user agreeing to it | A wizard that collects settings can end with everything configured and nothing working. `SetupPlan` gates on observations instead: a level above the noise floor, a model that *loaded*, words back from a real dictation. "Ready" means it worked. |
| The model gate wants the load as well as the download | A file on disk is not an engine — a missing runtime or a truncated download fails at initialisation. Calling that ready would be a lie told at the exact moment a new user is deciding whether this works. It also means the practice dictation is not the one waiting for a runtime to start for the first time. |
| The practice box is typed into by the real injector | Nothing puts the words on screen by hand. The same key, the same engine, the same delivery — so a broken injection shows up in the one place where someone is watching, rather than being counted as a success. |
| A settings file that already exists counts as set up | `SetupCompleted` is nullable, and `Migrate` reads null as "done". Onboarding is for a machine that has never run TawkType, not for everybody who updates to the version that added it. |
| `HotkeyCheck` never says a combination is free | Windows cannot be asked what a key is already bound to. It names the shortcuts nearly everyone has, refuses the keys TawkType already binds, and says nothing about the rest — an unknown combination comes back usable, not proven clear. |

## Measured numbers (owner's machine: i7-11700F, RTX 4060 Ti 8 GB)

Same 13 s clip, best of 5:

| Engine | Runs on | Load | Transcribe | Note |
|---|---|---|---|---|
| Parakeet int8 | CPU, 8 threads | 3.9 s | 931 ms | lower-cased one proper noun |
| Whisper large-v3-turbo | GPU (Vulkan) | 2.4 s | 413 ms | first-ever warm-up ~20 s (shader compile), then 0.4 s |

Both transcripts were otherwise identical and correctly punctuated.

## The rename

The app is **TawkType**, at [tawktype.com](https://tawktype.com). The previous working name was
already another dictation product's, and is a trademark of theirs. The design package is `docs/tawktype-brand/BRAND-PACKAGE.md`; the implementer's half is
`branding/BRAND.md`.

**It is now a complete rename.** The first pass changed only what a user reads and left six identifiers
alone because changing them risked real data. The owner asked for all of them, so each one was changed
*with a migration*, each exercised against a seeded old installation. Those migrations have since been
removed — see below — because nothing was ever installed for them to find:

| Identifier | Now | How an existing install survives |
|---|---|---|
| Data folder | `%LOCALAPPDATA%\TawkType` | Nothing to carry: no release was ever installed. The migration code that did carry it has been removed. |
| DPAPI entropy | `TawkType.ApiKey.v1` | No fallback. An `apikey.dat` written under the old name will not decrypt; the user is asked for the key again. |
| Run-key value | `TawkType` | No fallback. An entry under the old name, if one ever existed, would have to be removed by hand. |
| Window AUMID | `JupitorStudio.TawkType` | It only has to be an identity no shortcut claims, which this is. Re-check gotcha 20 on a real install. |
| Velopack packId | `TawkTypeApp` | **Does not migrate.** See below. The `App` suffix is what keeps it clear of the data folder. |
| Assembly, namespaces, projects, solution | `TawkType.*` | Internal; nothing outside the repo refers to them. |

The data folder sits flat under `%LOCALAPPDATA%` and the packId carries an `App` suffix, so the two
paths differ by that suffix alone. That is the whole of gotcha 19 and `DataFolderTests` reads the id
straight out of `build/pack.ps1` to assert it — shortening the packId to `TawkType` fails the build,
which was confirmed by trying it.

### Why the packId could be changed at all

A different packId is a different application to Velopack: anything installed under the old one polls
the old channel and would never update. Normally that makes the id effectively permanent.

**Nothing had ever been installed anywhere** — not by a user, not by the owner, who had been
uninstalling between tests. So there was no copy to strand, and the releases that carried the old ids
have since been deleted outright. That is the whole reason this was the moment: the cost of renaming a
package identity is zero before the first install and never zero again.

It moved twice. The original id to `TawkType` with the rename, then to `TawkTypeApp` when the data folder
was flattened to `%LOCALAPPDATA%\TawkType` and the two would otherwise have collided.

**The migration code has been removed**, along with the API-key and startup fallbacks that went with
it. It existed to carry data forward from three older folder names, and there is nothing anywhere to
carry: no release has been installed, and the owner has been uninstalling between tests. Anything
still sitting under an old name is a spent developer folder that can be deleted by hand.

### The old name appears nowhere, and that is a requirement

It belongs to another company. Treat any reappearance of it as a defect rather than as untidiness.

The three read-only values that carried it — the folders `LegacyMigration` read, the entropy old API
keys were encrypted with, and the old Run-key name — went with the migration code. There is no path
from a folder, key or startup entry under that name into this build, and none is wanted: nothing was
ever installed from a release, so there is nothing out there to rescue.

Seven things survived the bulk rename and were found later, by grepping case-*insensitively* across
every file type rather than trusting the claim above. That is the lesson: the first sweep matched one
capitalisation in `.cs` files, and not one of these was there.

- `Directory.Build.props` set the old name as `<Product>`, which the compiler stamps into every
  assembly — so the exe's Properties dialog and Task Manager both showed it.
- The vocabulary export offered it, lower-cased, as its default filename.
- `app.manifest` declared an `<assemblyIdentity>` under it. Vestigial for a .NET 8 app — the AUMID
  that matters to the taskbar is set in code, not here — but embedded in the binary all the same.
  **Worth a glance when the install of gotcha 20 is finally done**, since that fix was hard-won and
  this is the only identity string that has changed since it was made.
- The four taskbar-debugging environment variables, now `TAWKTYPE_TEST_AUMID`, `_ICONRES`,
  `_WINDOWID` and `_SKIPPROP`. Gotcha 20 names them, so it changed with them.
- A comment in `release.yml` naming the old packId.
- The worked example in `docs/FEATURE-RESEARCH-2026-09-11.md`, which used the name to illustrate a
  spelling rule.
- A citation note in `docs/REVIEW-2026-09-11.md` giving the old project paths.

The untracked working material in the repo folder was swept too, though git does not see it: the
design package's announcement copy, the website's own audit note, and a dead v1 deployment snapshot
under `website/.sites-runtime/` whose FAQ answered "why does the installer say [the old name]?" —
obsolete as well as unwanted, since that stopped being the install identity at v0.5.0. The current
site source and every snapshot from v2 on were already clean.

**Three things sit outside this repository:**

- **The checkout sits in a folder named after the old name** — `~/source/repos/<old name>`. Renaming
  it is a `git mv`-free, close-everything-first job: quit the app, close the IDE, rename the folder to
  `TawkType`, reopen. Nothing in the build refers to the folder by name.
- **The live site is not built from this repository.** tawktype.com is live and is produced by a
  separate site builder that has the new name; the untracked `website/` folder here is an earlier
  prototype, not the source of truth, and nothing in it is deployed. It was swept anyway.
- **Git history still carries it, and history is published.** 30 of the 69 commits name it in their
  message and 50 touched content containing it, all of it on GitHub. Only a rewrite would remove that
  — `git filter-repo` over messages and blobs, then a force push, which changes every SHA from the
  first commit on and breaks every existing clone and link. Nothing is installed from those releases
  and the repository is the owner's, so the cost is low, but it is a deliberate decision rather than
  tidying: **ask before doing it.**

## Gotchas the next person will hit

1. ~~Repo folder is still named `Murmur`.~~ Done — it is `~/source/repos/TawkType` now.
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
   seen a real rewrite or its latency yet. Run `tools/TawkType.Clean` with a real key first thing.
10. **`ModelStorage.Delete` only ever removes something `List()` reported**, so a caller cannot compose
    a path out of the models folder. Keep that property if you add another delete path.
11. **`TawkTypeSettings.Clone` is no longer a plain `MemberwiseClone`.** `Cleanup`, `History`, `Overlay` and `Appearance` are
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
    minimised the only ways back are the tray icon (left click, or "Show" on the menu) and
    turning Appearance → "Keep the pill on screen" from off to on.
19. **The packId must never name the folder that holds user data.** Velopack installs to
    `%LOCALAPPDATA%\<packId>` and *clears that folder first*. Early builds kept user data in
    `%LOCALAPPDATA%\<name>` and packed with that same `<name>` as the id, which destroyed settings,
    history and gigabytes of downloaded models before the app could run. It happened once, during development.

    Today the data folder is `%LOCALAPPDATA%\TawkType` and the id is `TawkTypeApp`. **They differ by
    that suffix and nothing else**, which is a thin margin for something this destructive — so
    `DataFolderTests` reads the id out of `build/pack.ps1` rather than restating it, and fails if the
    two ever name the same folder. Shortening the id to `TawkType` was tried, and does fail the build.

20. **Velopack's init takes over the taskbar button's icon.** `VelopackApp.Build().Run()` sets a
    process-wide AppUserModelID, and from then on Windows resolves the button's icon through that
    identity rather than `Window.Icon`, falling back to a generic one. Everything else still looks
    right — the exe's icon, the Start Menu shortcut, the hover thumbnail — which makes it read as an
    icon-cache problem. It is not.

    Fixed, but only by getting **both** of these right at once, which is why it took so long:
    - The window needs `PKEY_AppUserModel_RelaunchIconResource` **and** a
      `PKEY_AppUserModel_ID` of its own. Neither alone does anything.
    - That ID must be one **no installed shortcut claims** (`JupitorStudio.TawkType`). Set it to
      Velopack's own (`velopack.<packId>`) — which is what matching the process ID gives you, and what
      looks obviously correct — and the shell serves the icon registered for that app instead, i.e.
      the generic one. It ignores the property entirely.
    - `TaskbarWindow.StageIcon` copies the icon to `%TEMP%\TawkType\taskbar.ico` at every start and
      points the property there rather than at the executable. **That was justified by a measurement
      that turned out to be an artefact** — see gotcha 28 — and pointing the property straight at the
      installed executable may well work. It stays as it is pending a re-test against an install the
      user performed themselves; staging costs nothing and works either way.

    Also measured, so nobody re-derives it:
    - Cause confirmed by isolation: a build skipping only the Velopack call shows the correct icon.
    - `binary,-resourceId` (e.g. `TawkType.exe,-32512`) and `icon.ico,0` are both accepted forms. The
      `exe,0` index form that works in a shortcut resolves to nothing here.
    - `WM_SETICON` with `ExtractIconEx`, restarting Explorer, and a fresh uninstall/reinstall all
      change nothing. Neither does an unregistered *process* AUMID with no window property.
    - **How to iterate on this without packing an installer.** The whole difference between a plain
      build and an installed one is the process AUMID, so `TAWKTYPE_TEST_AUMID` makes `App` claim one at
      startup and reproduces the bug from `dotnet build`. `TAWKTYPE_TEST_ICONRES`,
      `TAWKTYPE_TEST_WINDOWID` and `TAWKTYPE_TEST_SKIPPROP` then vary the icon, the identity, or skip the
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
25. **`TawkType.Windows` sets `UseWPF` only for the UI Automation client assemblies.** It draws no UI.
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
    installer run from inside a session produces an install that looks perfect from in
    there and does not exist at all from outside: no install directory, no data folder, no `Uninstall`
    registry entry, and so no row in Programs and Features or in Settings. `%TEMP%`, `C:\` and the
    repo are not redirected, which is the only reason anything written there behaved normally.

    **Reads are the dangerous half, not writes.** The overlay serves back whatever a session — or an
    *earlier* session — wrote into it, and the result is indistinguishable from the real machine. A
    phantom install from a previous session reported a 129 MB install folder under the old packId
    complete with `Update.exe`, and registry uninstall entries to match. Acting on that produced
    confident, wrong advice about the user's own machine, twice in one session. The rule: **never
    describe the user's `%LOCALAPPDATA%`, `%APPDATA%` or `HKCU` back to them.** Give them a script and
    read its output.

    Three things follow, and they cost hours to learn:
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
35. **Only one TawkType can run per session** — a `Local\TawkType.SingleInstance` mutex. A second copy
    exits silently, so a scripted launch aimed at testing a new build will quietly drive the *old* one
    that is already up. Check for a running process before believing a screenshot.

36. **`SetForegroundWindow` returns before the switch has happened.** Typing immediately after it
    loses the first characters into whatever was still in front. `Win32WindowActivator` polls
    `GetForegroundWindow` until it matches, and gives up after 600 ms. Also: a process can only *give
    away* the foreground, never take it, so this works from a button in a window that is already in
    front and cannot be made to work from the background.
37. **Merging `App.xaml` into another `Application` throws.** Loading it constructs `TawkType.Desktop.App`,
    and WPF allows one `Application` per AppDomain — so a test harness that wants the app's windows has
    to supply the brand keys itself rather than merging the dictionary that defines them. Window
    `Icon` pack URIs resolve against the *entry* assembly too, so the harness needs its own copy of
    `tawktype.ico` as a `Resource`.

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
    the right names on screen while exposing `TawkType.Core.Settings.DictationMode` to every screen
    reader and every test script. `DictationMode` overrides `ToString()`; do the same for anything
    else that ends up in a list.

43. **A re-run of the release workflow used to fail on its own release.** The delta step fetches the
    most recent release for packaging; on a re-run that is the version being built, so vpk refused
    with "there is a release equal or greater to the current version" — naming a package the job had
    just downloaded. It showed up as failed runs against releases that had in fact published fine.
    The step skips its own tag now.

44. **Each release used to inherit every package before it.** The delta step downloaded `*.nupkg`
    from the previous release, vpk listed everything it found in the feed, and the publish step
    uploaded the whole folder — so the history compounded. v0.3.0 shipped **364 MB of assets and 17
    packages** for a 37 MB application, and v0.4.0 would have been larger again. It fetches only the
    previous *full* package now, which is all a delta needs; a simulated v0.4.0 produces three feed
    entries and 135 MB.

    Moot now — every release before v0.5.0 has been deleted outright, tags included, because nothing
    had ever been installed from one. **The fix in the workflow is what matters going forward**, and
    the warning it carries is this: deleting a published package breaks any installed copy polling
    that feed. It was free here only because there were no such copies. It will not be free again.

45. **The first-run meter and the engine share one microphone.** `SetupViewModel` opens the same
    `IAudioCapture` singleton the pipeline uses, purely to watch the level, and throws the clip away.
    If a dictation starts while that step is open the engine takes the device over — so the meter
    unsubscribes and *leaves the capture alone* rather than calling `Stop()`, which would take the
    user's recording away mid-sentence. The engine's clip then carries a little of the metered audio
    in front of it, which is harmless. Do not "tidy" that into an unconditional Stop.

46. **An empty `TextBox` with `MaxWidth` and `HorizontalAlignment="Left"` collapses to nothing.** It
    sizes itself to the text it does not have yet, so the practice box rendered about 40px wide and
    there was nothing visible to click into. `Width`, not `MaxWidth`, for a box that starts empty.
    Caught by screenshotting the step; the XAML compiles either way.

47. **A Velopack hook may not show UI, and is killed after 30 seconds.** Velopack's documentation is
    explicit on both: "you may not show any UI to the user", and "if your application receives one of
    these arguments and does not exit within the alloted time, it will be killed". So the obvious
    design for "delete my data?" — a dialog during the uninstall — is not available, and a MessageBox
    there would hang on anyone who walked away mid-answer. The question is asked in Settings instead
    and `OnBeforeUninstallFastCallback` only obeys it. If you ever do want a prompt at uninstall time,
    the hook has to spawn a detached copy from `%TEMP%` and return immediately.

48. **Never delete a folder without `DataRemoval.Check`.** Two callers delete the data folder
    recursively and one of them runs during an uninstall with nobody watching, while Velopack clears
    a folder whose name differs by three letters. The check is pure and tested for exactly that: the
    install folder, any folder containing it, anything near the root of a drive, and any relative
    path. It also checks rootedness *before* normalising, because `Path.GetFullPath` resolves a
    relative path against the current directory and would otherwise let `TawkType\models` through as
    a real folder somewhere else.

49. **The Bash tool's heredocs eat backslashes, so scripts with paths or escapes belong in a file.**
    `python - <<'PY'` looks quoted and is not reliable here: `\\n` inside the script arrived as a real
    newline, which silently corrupted a C# string literal into a multi-line one, and `%TEMP%\\Tawk…`
    became a `\u` escape error. Three edits failed this way before the pattern was obvious. Write the
    script with the Write tool and run it by path — the Python file itself is fine, it is the heredoc
    that is not.

## Roadmap, in the order I would do it

1. **Cut a release from `main` and install it.** Nothing has ever been installed from any release, so
   this is a first install with no old copy to remove — and `main` is four commits past `v0.5.0`, so
   the tag contains none of the work below.

   Three code paths have never executed anywhere, and one install exercises all three:

   - **The taskbar icon** (gotcha 20). Fixed by a long hunt, never once tested against a real install.
     Re-check it particularly because the manifest's assembly identity changed with the name sweep —
     the only identity string that has moved since that fix was made.
   - **The first run's practice dictation.** Every other step was driven and screenshotted here; that
     one needs a voice. A clean machine with no `settings.json` opens setup by itself.
   - **The uninstall hook.** Tick *Delete all of this if I uninstall TawkType* in Settings, uninstall,
     and read `%TEMP%\TawkType\uninstall.log`. Then install again, leave the box unticked, uninstall,
     and check the data folder survived — that is the default and the more important half.

   Plus what an install has always been for: the Start Menu entry, the tray, downloading a model, and
   a dictation landing in another application.

   An agent session cannot do any of it. Writes under `%LOCALAPPDATA%` and `HKCU` go into a
   per-session overlay (gotcha 28), so everything verified here was verified there and nowhere else.

2. **Close review finding 6, the clipboard.** The restore races the paste and only text is put back,
   so an image or formatted content is destroyed by a dictation — more likely now that multiline
   results always paste. It is the largest fully-open finding and it is written up with a reproduction.

   Then the rest of finding 10: the filler regex still removes German "um" and a lower-case English
   "er". All-capitals words are protected, which is why *"The ER is open"* survives, but a
   capitalisation rule cannot reach the lower-case collisions — that needs the language.
3. **Prove the rewrite on real dictation** and tune `CleanupPrompt` against it. Still true: nobody has
   seen a successful call (gotcha 9), so every judgement about rewrite quality is currently a guess.
4. **Finish the brand assets**: high-contrast tray variants, outlined SVG wordmarks, and a licence and
   trademark check on the name. The domain is bought; availability was never established.
5. **Bring Remember… to the dictation box.** It is in the history window; the box is the other place
   the wrong words are already on screen.
6. **Per-app modes**: read the foreground window's process name at release time and pick a mode from
   it. `FocusTarget.ProcessName` is already captured at key-down, so this is a map and a settings page.
   Feature research §5 calls it the "later" half of modes.
7. **Visible privacy** (§9): a panel that shows what is actually kept during use, rather than what
   the settings imply. First run states it once, at the end, from what actually happened — but that
   is a sentence at a moment, not an indicator while dictating.
8. **Streaming partials** while the key is held. Parakeet is a transducer; it suits this.
9. **Local `ILlmClient`** so the rewrite works offline and "nothing leaves this machine" holds with
   cleanup switched on.
10. **Command mode**: hold a second key, speak an instruction, replace the selected text.

## Session log (what was actually done, in order)

1. Researched Wispr Flow and engine options; profiled the machine; chose .NET 8 + WPF.
2. Scaffolded five projects; wrote Core pipeline + tests; Windows hook/audio/injection; Whisper.net.
3. Built the WPF app: tray, overlay, settings, file logging. Verified with screenshots from the running app.
4. Added Parakeet via sherpa-onnx, the engine router and selection tests, and a two-engine benchmark.
5. Added model deletion (Settings + tray) with engine unload first. Committed.
6. Renamed everything to TawkType; designed the mark, palette, and wordmark; built the brand render tool;
   restyled the overlay and settings; added the legacy data migration. Committed.
7. Added the LLM cleanup pass: `ILlmClient` + `IApiKeyStore` in Core, `LlmTextCleaner` with its timeout
   and fallbacks, `CleanupPrompt`, the `TawkType.Llm` project with `ClaudeLlmClient`, DPAPI key storage,
   a `Polishing` pipeline state, the Settings expander, `tools/TawkType.Clean`, and 20 more tests.
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
26. Rebranded to TawkType: name, mark, palette and overlay wording from the design package, with the
    update identity, the data folder and the DPAPI entropy deliberately left alone at first.
27. Renamed the GitHub repository to TawkType and pointed the update feed at it; bought tawktype.com
    and put it in the README, the About panel and the brand notes.
28. Finished the rename on the owner's instruction: data folder, DPAPI entropy, Run-key value, window
    AppUserModelID, assembly, namespaces, projects, solution, installer. Each identifier got a
    migration, and each migration was exercised against a seeded old installation.
29. Flattened the data folder to `%LOCALAPPDATA%\TawkType` and moved the packId to `TawkTypeApp` to keep
    it clear of the install directory, then deleted every migration once it was established that no
    release had ever been installed anywhere.
30. Corrected the documentation against the code rather than memory: the model download is not
    automatic, Whisper runs Vulkan-then-CPU with no CUDA, and both model sizes were quoted from the
    progress-bar estimates instead of the files. Measured what each engine costs to run and put it in
    the README.
31. Cut v0.5.0, then deleted every earlier release and tag — safe exactly once, while nothing had been
    installed from any of them.
32. Built the first-run experience (feature research §8): `SetupPlan`, `HotkeyCheck` and
    `MicrophoneCheck` in Core with 41 tests, a seven-step `SetupWindow` that saves as it goes, a
    `SetupCompleted` flag whose migration treats an existing settings file as already set up, and a
    tray item and `--setup` flag to open it again. Drove every step of the running window and
    screenshotted it — including a real 78 MB download and warm-up — except the practice dictation,
    which needs a voice.
33. Took the previous working name out of the repository entirely, on the owner's instruction: it is
    another company's trademark. Seven survivors of the bulk rename — the `Product` stamped into
    every assembly, the vocabulary export's default filename, the manifest's assembly identity, the
    four taskbar-debugging environment variables, a workflow comment, a worked example in the feature
    research, and a citation note in the review. Found by grepping case-insensitively across every
    file type rather than trusting the claim that none were left.
34. Licensed it: `LICENSE.md`, source available rather than open source — unrestricted free use of
    the application, no redistribution, no derivative works, name and mark reserved, contributions
    assigned. README, `THIRD-PARTY-NOTICES.md` and the About panel say so and point at it. Written
    rather than adopted, because the obvious off-the-shelf candidate turned out to be noncommercial.
35. Made it possible to take the data with the uninstall: a setting on Settings → General, a
    *Delete my data…* button beside it, `AppDataMaintenance` doing the work, and Velopack's
    `OnBeforeUninstallFastCallback` obeying the setting silently. `DataRemoval` guards both callers.
    The button and its confirmation were driven in the running app; the hook cannot be tested from a
    session that cannot install anything.
36. Gave uninstalling its own section in the README — how, and why the question has to be asked
    beforehand — and found a defect while writing it: nothing removed the start-with-Windows registry
    value on uninstall, because TawkType writes it rather than Velopack, so every sign-in afterwards
    would try to launch a deleted executable. The hook clears it now, whatever the user chose about
    their data.

## Links

- Parakeet model: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 (sherpa-onnx int8 export:
  https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8)
- Whisper.net: https://github.com/sandrohanea/whisper.net
- sherpa-onnx: https://github.com/k2-fsa/sherpa-onnx
- Reference product: https://wisprflow.ai
