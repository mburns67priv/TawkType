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
                                               ──► ITextInjector.InjectAsync
```

`DictationEngine` (in `Talk2Me.Core`) owns the state machine:

```
Idle ──press──► Listening ──release──► Transcribing ──► Injecting ──► Idle
                                            │
                                         failure ──► Error ──► Idle
```

Rules: presses while busy are ignored; taps shorter than `MinimumHoldMs` are dropped; the release handler
hops off the hook thread immediately because low-level hooks are killed by Windows if they stall.

Every stage is an interface so each can be swapped independently:

| Interface | Today | Later |
|---|---|---|
| `IPushToTalkHotkey` | keyboard hook | mouse side-button, foot pedal, double-tap toggle mode |
| `IAudioCapture` | WaveIn 16 kHz | WASAPI shared-mode with own resampler, VAD auto-stop |
| `ITranscriber` | router → Parakeet (sherpa-onnx) or Whisper.net | streaming partials while the key is held, Parakeet on GPU via a CUDA sherpa-onnx build |
| `ITextCleaner` | regex fillers | local LLM / Claude API rewrite: tone per app, lists, spoken corrections, personal dictionary |
| `ITextInjector` | SendInput / clipboard | UI Automation `TextPattern` for exact caret insertion |

## Threads

- **UI thread**: WPF, tray icon, the keyboard hook (hooks need a message loop), overlay updates via
  `Dispatcher.BeginInvoke`.
- **WaveIn callback thread**: appends samples under a lock, raises level events.
- **Thread pool**: transcription, cleanup, injection (`Task.Run` from the release handler).
- Whisper's processor is not thread-safe; `WhisperTranscriber` serialises calls with a semaphore.

## Settings

`%LOCALAPPDATA%\Talk2Me\settings.json`, loaded once at startup by `SettingsStore` and re-read by consumers
through `ISettingsProvider.Current`, so a save takes effect without a restart: the hotkey re-resolves on
`Changed`, the transcriber reloads when model or language differ from what is loaded, audio device is
resolved at each `Start()`.

## Roadmap

1. **LLM cleanup** behind `ITextCleaner`: filler removal is regex today; the Wispr-style "say what you
   meant" rewrite (corrections, lists, tone per app) needs a model. Options: Claude API (best quality),
   or a small local model via ONNX / llama.cpp for offline.
2. **Per-app styles**: detect the foreground window's process name, pick a tone preset (chat vs email vs
   code editor).
3. **Personal dictionary**: Whisper `initial_prompt` seeded with the user's words; learn from corrections.
4. **Command mode**: select text, hold a second key, speak an instruction, replace selection.
5. **Streaming**: transcribe in 1-second windows while the key is held so text appears as you speak.
6. **Branding, continued**: identity, icon, palette and overlay restyle are done (see
   `branding/BRAND.md`). Still to do: overlay waveform animation, onboarding window, installer (MSIX or
   Velopack), auto-update.
7. **Auto-start** with Windows, single-instance guard, crash recovery.
