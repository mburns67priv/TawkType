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
IPushToTalkHotkey ──Pressed──► DictationEngine ──► IAudioCapture.Start()
                  ──Released─►                 ──► IAudioCapture.Stop() ──► AudioClip
                                               ──► ITranscriber.TranscribeAsync ──► TranscriptResult
                                               ──► ITextCleaner.CleanAsync ──► string
                                                     └─► ILlmClient.CompleteAsync (optional, timed out)
                                               ──► ITextInjector.InjectAsync
```

`DictationEngine` (in `Talk2Me.Core`) owns the state machine:

```
Idle ──press──► Listening ──release──► Transcribing ──► [Polishing] ──► Injecting ──► Idle
                                            │
                                         failure ──► Error ──► Idle
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
`%LOCALAPPDATA%\Talk2Mepikey.dat`, encrypted by `DpapiApiKeyStore` with DPAPI under the current user
(`ANTHROPIC_API_KEY` is the fallback). That protects the file at rest against other accounts on the
machine — not against anything running as this user.

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

Between dictations `OverlayViewModel` settles rather than hides: `IsResting` goes true, the text becomes
the hotkey hint, and the view fades the pill to `Overlay.RestingOpacity`. `Overlay.AlwaysVisible = false`
restores the old hide-when-idle behaviour.

## History

`DictationHistoryStore` (Core) appends one JSON object per dictation to
`%LOCALAPPDATA%\Talk2Me\history.jsonl`. Append-only is the point: a dictation must never be lost or
delayed by the log, so the common path is one `File.AppendAllText`, and any failure there is logged and
swallowed — the text has already been typed by then.

The whole file is rewritten only when trimming past `History.MaxEntries` (plus slack, so a rewrite is not
on every add) or clearing. `Recent` caps at the configured maximum regardless of what is still on disk.
A line torn by a crash mid-write is skipped at load rather than failing the file.

`HistoryWindow` subscribes through `IDictationHistory.Changed`, so it updates live while it sits on
screen. Its list expands rows in place rather than pairing a list with a detail pane: in a 420x560 panel
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

`%LOCALAPPDATA%\Talk2Me\settings.json`, loaded once at startup by `SettingsStore` and re-read by consumers
through `ISettingsProvider.Current`, so a save takes effect without a restart: the hotkey re-resolves on
`Changed`, the transcriber reloads when model or language differ from what is loaded, audio device is
resolved at each `Start()`.

## Roadmap

1. ~~**LLM cleanup** behind `ITextCleaner`~~ — done, Claude-backed and off by default. Still open: a
   local backend behind `ILlmClient` for an offline rewrite, and streaming the rewrite so the first
   words are typed before the last ones arrive.
2. **Per-app styles**: detect the foreground window's process name, pick a tone preset (chat vs email vs
   code editor).
3. **Personal dictionary**: the cleanup prompt takes a word list today. Next: seed Whisper's
   `initial_prompt` with it too, and learn new entries from what the user corrects by hand.
4. **Command mode**: select text, hold a second key, speak an instruction, replace selection.
5. **Streaming**: transcribe in 1-second windows while the key is held so text appears as you speak.
6. **Branding, continued**: identity, icon, palette and overlay restyle are done (see
   `branding/BRAND.md`). Still to do: overlay waveform animation, onboarding window, installer (MSIX or
   Velopack), auto-update.
7. **Auto-start** with Windows, single-instance guard, crash recovery.
