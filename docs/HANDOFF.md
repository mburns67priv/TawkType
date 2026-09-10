# Talk2Me — handoff

Written 2026-09-10 at the end of the first build session. Read this first; then `README.md` for usage,
`docs/ARCHITECTURE.md` for design, `branding/BRAND.md` for the identity.

## What this is

Push-to-talk dictation for Windows, a Wispr Flow clone. Hold Right Ctrl, speak, release; the cleaned-up
text is typed into whatever has focus. Everything runs locally. There is no cloud, account, or telemetry.

Owner: Jupitor Studio. Working name was **Murmur**; it is now **Talk2Me**.

## State of the code

- **Branch `main`, 2 commits, clean tree.** `9e84eea` skeleton + both engines; `6f61150` rename + brand.
- **Builds clean** with `dotnet build`, **21 unit tests pass** with `dotnet test`.
- **Works end to end on real hardware.** The owner's own mic test: 3.2 s of speech → typed in 215 ms
  with Parakeet. Overlay, tray, settings, model download, model deletion are all verified in the running
  app.
- Not yet done: installer, auto-start, single-instance guard, LLM cleanup, streaming, onboarding.

## Repo map

```
Talk2Me.sln
src/Talk2Me.Core            pipeline state machine, interfaces, settings, regex cleaner  (no Windows deps)
src/Talk2Me.Windows         WH_KEYBOARD_LL hook, WaveIn mic capture, SendInput + clipboard injection
src/Talk2Me.Transcription   ParakeetTranscriber (sherpa-onnx), WhisperTranscriber (Whisper.net),
                            TranscriberRouter, model downloaders, ModelStorage
src/Talk2Me.App             WPF tray app (namespace Talk2Me.Desktop): App.xaml has the palette + mark
                            geometry; Views/ has OverlayWindow, SettingsWindow, BrandMark; Services/ has
                            ModelMaintenance and LegacyMigration; Logging/ has the file logger
tools/Talk2Me.Bench         transcribes a WAV with one or both engines, prints latency side by side
tools/Talk2Me.Brand         renders talk2me.ico + logo PNGs from the vector mark (WPF, no external tools)
tests/Talk2Me.Core.Tests    xUnit: DictationEngine, BasicTextCleaner, EngineSelection
branding/                   BRAND.md, mark.svg, icon.svg, logo.svg, exports/
docs/                       ARCHITECTURE.md, HANDOFF.md
```

## Run, build, test

```bash
dotnet run --project src/Talk2Me.App          # tray app; first run downloads the active engine's model
dotnet test                                   # 21 tests, < 1 s
dotnet run --project tools/Talk2Me.Bench -- speech.wav Both 5
dotnet run --project tools/Talk2Me.Brand      # regenerate icon + exports after brand changes
```

Dev launch flags: `--settings` opens Settings at start; `--overlay-demo` cycles the overlay through every
state so it can be styled without dictating.

Requirements: Windows 10/11, .NET 8 SDK. GPU optional. No CUDA Toolkit, no Rust, no Python.

## Where things live at runtime

`%LOCALAPPDATA%\Talk2Me\`

- `settings.json` — all user settings; saved from the Settings window, hot-reloaded by every consumer.
- `models\ggml-large-v3-turbo.bin` (1.6 GB) and `models\parakeet-tdt-0.6b-v3-int8\` (640 MB).
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
| Regex filler cleanup only | Placeholder behind `ITextCleaner`. The Wispr-style rewrite is the next big feature (see roadmap). |
| Brand assets rendered by a WPF tool | Same geometry as the in-app XAML, zero external dependencies, reproducible from `dotnet run`. |

## Measured numbers (owner's machine: i7-11700F, RTX 4060 Ti 8 GB)

Same 13 s clip, best of 5:

| Engine | Runs on | Load | Transcribe | Note |
|---|---|---|---|---|
| Parakeet int8 | CPU, 8 threads | 3.9 s | 931 ms | lower-cased one proper noun |
| Whisper large-v3-turbo | GPU (Vulkan) | 2.4 s | 413 ms | first-ever warm-up ~20 s (shader compile), then 0.4 s |

Both transcripts were otherwise identical and correctly punctuated.

## Gotchas the next person will hit

1. **Repo folder is still named `Murmur`.** Everything inside is Talk2Me. Rename the folder when no
   session or terminal has it open: `mv ~/source/repos/Murmur ~/source/repos/Talk2Me`.
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

## Roadmap, in the order I would do it

1. **LLM cleanup** (`ITextCleaner`): Wispr's real magic is the rewrite. Spoken corrections ("no, make that
   Tuesday"), list formatting, tone per app, personal dictionary. Claude API for quality, or a small local
   model for offline. The interface, DI slot, and settings plumbing already exist.
2. **Per-app tone**: read the foreground window's process name at release time, pick a preset.
3. **Installer + auto-start + single instance**: Velopack is the least friction for a .NET tray app;
   MSIX if Store distribution matters.
4. **Streaming partials** while the key is held (Parakeet is a transducer; it suits this).
5. **Overlay polish**: replace the level bar with an animated waveform; onboarding window on first run.
6. **Command mode**: hold a second key, speak an instruction, replace the selected text.

## Session log (what was actually done, in order)

1. Researched Wispr Flow and engine options; profiled the machine; chose .NET 8 + WPF.
2. Scaffolded five projects; wrote Core pipeline + tests; Windows hook/audio/injection; Whisper.net.
3. Built the WPF app: tray, overlay, settings, file logging. Verified with screenshots from the running app.
4. Added Parakeet via sherpa-onnx, the engine router and selection tests, and a two-engine benchmark.
5. Added model deletion (Settings + tray) with engine unload first. Committed.
6. Renamed everything to Talk2Me; designed the mark, palette, and wordmark; built the brand render tool;
   restyled the overlay and settings; added the legacy data migration. Committed.

## Contacts and links

- Owner: servers@jupitorstudio.com
- Parakeet model: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 (sherpa-onnx int8 export:
  https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8)
- Whisper.net: https://github.com/sandrohanea/whisper.net
- sherpa-onnx: https://github.com/k2-fsa/sherpa-onnx
- Reference product: https://wisprflow.ai
