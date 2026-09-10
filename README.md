# Murmur

Push-to-talk dictation for Windows, in the spirit of Wispr Flow: hold a key, speak, release, and clean
text is typed into whatever you were working in. Everything runs locally on your GPU. No cloud, no
subscription, no telemetry.

> "Murmur" is a working codename. Branding comes later; rename with a find-and-replace on `Murmur`.

## How it works

```
hold key ──► mic capture (16 kHz) ──► release ──► Whisper (GPU) ──► cleanup ──► type into focused app
             ▲ floating pill shows "Listening…" / "Transcribing…" / "Typing…"
```

| Piece | Implementation |
|---|---|
| Hotkey | `WH_KEYBOARD_LL` hook, so we get key-up as well as key-down system-wide |
| Audio | NAudio WaveIn at 16 kHz mono, exactly what Whisper wants |
| Speech-to-text | Two engines behind one interface: NVIDIA Parakeet TDT 0.6B v3 (sherpa-onnx, CPU int8) for English and 24 other European languages, Whisper.net `large-v3-turbo` (CUDA 12 → Vulkan → CPU) for the rest |
| Cleanup | Filler removal, whitespace, casing. LLM rewrite step is the next milestone |
| Typing | `SendInput` Unicode events; clipboard paste for long text |
| UI | WPF: tray icon, click-through overlay pill, settings window |

## Run it

Requirements: Windows 10/11, .NET 8 SDK, an NVIDIA/AMD GPU with a current driver (Vulkan). The CUDA
Toolkit 12.4+ is optional and makes transcription faster on NVIDIA cards.

```bash
dotnet run --project src/Murmur.App
```

First launch downloads the model for the active engine into `%LOCALAPPDATA%\Murmur\models` (Parakeet
~670 MB, Whisper ~1.6 GB) and shows progress in the overlay. Then:

1. Click into any text field.
2. Hold **Right Ctrl**, speak, release.
3. Text appears about half a second later.

Right-click the tray icon for **Settings** (hotkey, microphone, model, language, injection mode) or
**Test dictation**, which records 3 seconds without needing the hotkey.

Logs: `%LOCALAPPDATA%\Murmur\logs\murmur.log`.

Launch flags for development: `--settings` opens the settings window immediately; `--overlay-demo`
cycles the overlay pill through every state (listening with a fake mic level, transcribing, typing,
error, model download) so it can be styled without dictating.

## Measured on the reference machine (i7-11700F, RTX 4060 Ti)

Same 13 s clip, best of 5 runs, `tools/Murmur.Bench`:

| Engine | Where it runs | Model load | Transcribe 13 s | Transcript |
|---|---|---|---|---|
| Parakeet TDT 0.6B v3 int8 | CPU, 8 threads | 3.9 s | 931 ms (14× real-time) | identical, "murmur" lower-cased |
| Whisper large-v3-turbo | GPU via Vulkan | 2.4 s | 413 ms (31× real-time) | identical |

Notes:

- Whisper's first-ever warm-up compiles Vulkan shaders (~20 s once per driver); later launches warm up in
  under half a second.
- Parakeet on CPU is slower than Whisper on this GPU, but it needs no GPU at all, does not hallucinate on
  silence, and benchmarks more accurately on real (non-synthetic) English speech. Both feel instant for
  typical 3–8 s dictations. Switch with the **Engine** setting.
- Installing the CUDA Toolkit 12.4+ moves Whisper to CUDA automatically. Parakeet on GPU would need the
  CUDA build of sherpa-onnx, which is not on NuGet.

## Benchmark a WAV

```bash
dotnet run --project tools/Murmur.Bench -- path\to\speech.wav Both 5
```

Arguments: `<wav> [engine=Both|Parakeet|Whisper] [runs] [language] [whisperModel]`. Prints warm-up time,
per-run latency, the transcript from each engine, and a side-by-side summary.

## Tests

```bash
dotnet test
```

## Layout

```
src/Murmur.Core            pipeline, abstractions, settings   (no Windows dependencies, fully unit-tested)
src/Murmur.Windows         keyboard hook, WaveIn capture, SendInput / clipboard injection
src/Murmur.Transcription   Whisper.net transcriber + model download
src/Murmur.App             WPF tray app, overlay, settings (namespace Murmur.Desktop)
tools/Murmur.Bench         console harness for latency / backend checks
tests/Murmur.Core.Tests    xUnit
docs/ARCHITECTURE.md       design notes and roadmap
```
