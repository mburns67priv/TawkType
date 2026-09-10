# Talk2Me — handoff

Written 2026-09-10 at the end of the first build session, updated later the same day when the LLM
cleanup pass landed. Read this first; then `README.md` for usage,
`docs/ARCHITECTURE.md` for design, `branding/BRAND.md` for the identity.

## What this is

Push-to-talk dictation for Windows, a Wispr Flow clone. Hold Right Ctrl, speak, release; the cleaned-up
text is typed into whatever has focus. Everything runs locally. There is no cloud, account, or telemetry.

Owner: Jupitor Studio. Working name was **Murmur**; it is now **Talk2Me**.

## State of the code

- **Branch `main`, clean tree.** `9e84eea` skeleton + both engines; `6f61150` rename + brand;
  `12d8117` this doc; then the LLM cleanup pass.
- **Builds clean** with `dotnet build`, **76 unit tests pass** with `dotnet test`.
- **Works end to end on real hardware.** The owner's own mic test: 3.2 s of speech → typed in 215 ms
  with Parakeet. Overlay, tray, settings, model download, model deletion are all verified in the running
  app.
- **LLM cleanup is in**, off by default: `LlmTextCleaner` runs the regex cleaner, then optionally a
  Claude rewrite under a 2 s timeout, falling back to the regex text on anything that goes wrong. Unit
  tested; the live path was verified only as far as a rejected key (see "Gotchas" 9).
- Not yet done: installer, auto-start, single-instance guard, streaming, onboarding, a local LLM backend.

## Repo map

```
Talk2Me.sln
src/Talk2Me.Core            pipeline state machine, interfaces, settings, regex cleaner  (no Windows deps)
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
tests/Talk2Me.Core.Tests    xUnit: DictationEngine, BasicTextCleaner, EngineSelection, LlmTextCleaner,
                            CleanupPrompt, ModelStorage, DictationHistoryStore, settings cloning
branding/                   BRAND.md, mark.svg, icon.svg, logo.svg, exports/
docs/                       ARCHITECTURE.md, HANDOFF.md
```

## Run, build, test

```bash
dotnet run --project src/Talk2Me.App          # tray app; first run downloads the active engine's model
dotnet test                                   # 76 tests, < 1 s
dotnet run --project tools/Talk2Me.Clean -- "um the deadline is monday no wait tuesday"
dotnet run --project tools/Talk2Me.Bench -- speech.wav Both 5
dotnet run --project tools/Talk2Me.Brand      # regenerate icon + exports after brand changes
```

Dev launch flags: `--settings` opens Settings at start; `--history` opens the history window;
`--overlay-demo` cycles the overlay through every state so it can be styled without dictating.

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
- `history.jsonl` — every dictation, one JSON object per line, capped at 200. **Plain text**: this is
  everything the user has ever dictated. `History.Enabled` turns it off.
- `logs\talk2me.log` — rolling 5 MB. Debug level. Every dictation logs chars, audio seconds, and ms.

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
| Settings is a nav rail + pages, not one form | It had grown past 1400px with the expanders open and was genuinely hard to read. Six pages (General / Transcription / Activation / Appearance / AI cleanup / History) modelled on WhisperTyping, which the owner asked for by screenshot. |
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
20. **`TaskbarWindow` must keep a normal window style.** It looks like it wants
    `WindowStyle="None"` + `AllowsTransparency` since it is never meant to be seen, but that stops WPF
    applying `Window.Icon` and the taskbar button falls back to a generic Windows icon. Being 1x1 at
    -32000 and always minimised is what keeps it invisible.
21. **Do not re-add `WS_EX_TRANSPARENT` to `OverlayWindow`.** It would make the toolbar unclickable and
    dragging impossible. `WS_EX_NOACTIVATE` is what keeps focus where it belongs.
22. **Never check a remembered window position against `SystemParameters.VirtualScreen*`.** That is the
    bounding box of every monitor, and on the owner's four-monitor layout it contains regions no monitor
    covers. Use `WindowPlacement.Restore` with `MonitorLayout.WorkAreas()`.
23. **`IsCancel="True"` does nothing on a modeless window.** WPF's cancel handling sets `DialogResult`,
    which only applies to a window shown with `ShowDialog`. Settings is shown with `Show`, so its Cancel
    button needs a real `Click` handler and Escape needs wiring by hand — via bubbling `OnKeyDown`, not
    `OnPreviewKeyDown`, so an open combo box dropdown still gets Escape first.
24. **`Talk2Me.Windows` sets `UseWPF` only for the UI Automation client assemblies.** It draws no UI.
    Turning it on also changed the implicit usings, which is why `DpapiApiKeyStore` now imports
    `System.IO` explicitly.
25. **Chromium reports its page body as a read-only Document and a focused input as an Edit.** That is
    the discrimination the probe relies on, and it was verified against real Chrome — check it again if
    the control-type rules are ever touched, because getting it wrong breaks dictation into web forms.
26. **`Overlay.WindowLeft/Top` and `History.WindowLeft/Top` are physical pixels, not WPF units.**
    `History.WindowWidth/Height` are still WPF units. Values saved before this change were WPF units;
    they differ only under DPI scaling, and a wrong one is clamped on the next launch rather than lost.

## Roadmap, in the order I would do it

1. **Prove the rewrite on real dictation** and tune the prompt in `CleanupPrompt` against it. Everything
   below is guesswork until someone has used it for a day.
2. **Per-app tone**: read the foreground window's process name at release time, pick a preset. The
   `CleanupStyle` setting and prompt seam are already there; this just chooses the value per app.
3. **Installer + auto-start + single instance**: Velopack is the least friction for a .NET tray app;
   MSIX if Store distribution matters.
4. **Streaming partials** while the key is held (Parakeet is a transducer; it suits this).
5. **Overlay polish**: replace the level bar with an animated waveform; onboarding window on first run.
6. **Command mode**: hold a second key, speak an instruction, replace the selected text.
7. **Local LLM backend** behind `ILlmClient`, so the rewrite works offline and the "nothing leaves this
   machine" promise holds with cleanup switched on.

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

## Links

- Parakeet model: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 (sherpa-onnx int8 export:
  https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8)
- Whisper.net: https://github.com/sandrohanea/whisper.net
- sherpa-onnx: https://github.com/k2-fsa/sherpa-onnx
- Reference product: https://wisprflow.ai
