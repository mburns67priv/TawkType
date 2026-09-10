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

## The overlay pill

The pill is the one window that is on screen while the user works in something else, so its whole design
is about not interfering:

- `WS_EX_NOACTIVATE` — Windows will not activate it, so clicking near it cannot move focus.
- `WS_EX_TRANSPARENT` plus `IsHitTestVisible="False"` — clicks go through to the window behind.
- `ShowActivated="False"` — showing it does not pull focus off the user's window.
- Raised with `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)`. Never `Activate()` or
  `SetForegroundWindow` — either would move focus, which is the one thing this window must not do.

`Topmost="True"` alone is not enough: another topmost window put up later sits above it. So the pill
re-asserts its z-order every time it goes from resting to active, which is exactly when the user needs
to see it.

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
screen. Closing it hides it; only shutdown really closes it. Its placement and always-on-top state live
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
