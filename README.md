# Talk2Me

Push-to-talk dictation for Windows, in the spirit of Wispr Flow: hold a key, speak, release, and clean
text is typed into whatever you were working in. Speech never leaves your machine: capture and
transcription are entirely local. No subscription, no telemetry. The one optional exception is the AI
cleanup pass, which is off until you turn it on and explain itself below.

## How it works

```
hold key ──► mic capture (16 kHz) ──► release ──► Parakeet / Whisper ──► cleanup ──► type into focused app
             ▲ bar rests on screen, then shows "Listening" + waveform + timer, "Transcribing…", "Typing…"
```

| Piece | Implementation |
|---|---|
| Hotkey | `WH_KEYBOARD_LL` hook, so we get key-up as well as key-down system-wide |
| Audio | NAudio WaveIn at 16 kHz mono, exactly what Whisper wants |
| Speech-to-text | Two engines behind one interface: NVIDIA Parakeet TDT 0.6B v3 (sherpa-onnx, CPU int8) for English and 24 other European languages, Whisper.net `large-v3-turbo` (CUDA 12 → Vulkan → CPU) for the rest |
| Cleanup | Regex filler removal, whitespace, casing — always. Optionally a Claude rewrite on top: spoken corrections, lists, personal dictionary, tone |
| Typing | `SendInput` Unicode events; clipboard paste for long text |
| UI | WPF: tray icon, a draggable always-on-screen status bar with its own toolbar, a six-page settings window, history window. Light and dark themes, or follow Windows |

## Run it

Requirements: Windows 10/11, .NET 8 SDK, an NVIDIA/AMD GPU with a current driver (Vulkan). The CUDA
Toolkit 12.4+ is optional and makes transcription faster on NVIDIA cards.

```bash
dotnet run --project src/Talk2Me.App
```

First launch downloads the model for the active engine into `%LOCALAPPDATA%\Talk2Me\models` (Parakeet
~670 MB, Whisper ~1.6 GB) and shows progress in the overlay. Then:

1. Click into any text field.
2. Hold **Right Ctrl**, speak, release.
3. Text appears about half a second later.

Right-click the tray icon for **Settings** (hotkey, microphone, model, language, injection mode) or
**Test dictation**, which records 3 seconds without needing the hotkey.

Logs: `%LOCALAPPDATA%\Talk2Me\logs\talk2me.log`.

Launch flags for development: `--settings` opens the settings window immediately; `--overlay-demo`
cycles the overlay pill through every state (listening with a fake mic level, transcribing, typing,
error, model download) so it can be styled without dictating.

## Measured on the reference machine (i7-11700F, RTX 4060 Ti)

Same 13 s clip, best of 5 runs, `tools/Talk2Me.Bench`:

| Engine | Where it runs | Model load | Transcribe 13 s | Transcript |
|---|---|---|---|---|
| Parakeet TDT 0.6B v3 int8 | CPU, 8 threads | 3.9 s | 931 ms (14× real-time) | identical, one proper noun lower-cased |
| Whisper large-v3-turbo | GPU via Vulkan | 2.4 s | 413 ms (31× real-time) | identical |

Notes:

- Whisper's first-ever warm-up compiles Vulkan shaders (~20 s once per driver); later launches warm up in
  under half a second.
- Parakeet on CPU is slower than Whisper on this GPU, but it needs no GPU at all, does not hallucinate on
  silence, and benchmarks more accurately on real (non-synthetic) English speech. Both feel instant for
  typical 3–8 s dictations. Switch with the **Engine** setting.
- Installing the CUDA Toolkit 12.4+ moves Whisper to CUDA automatically. Parakeet on GPU would need the
  CUDA build of sherpa-onnx, which is not on NuGet.

## Settings

Settings is a nav rail plus pages rather than one long form: **General** (overview cards and your stats),
**Transcription**, **Activation**, **Appearance**, **AI cleanup**, **History**.

General shows totals from the dictation log — dictations, speech duration, average words per minute,
total words and characters, and time saved against typing the same words at 40 wpm.

**Appearance** picks Light, Dark, or System, which follows the Windows app theme and keeps following it
if you change it. The change is live: open windows restyle without reopening. The status pill is
deliberately excluded — it floats over other applications, so it stays dark in every theme.

## The status bar

The bar stays on screen. Between dictations it rests dimmed, showing your hotkey; the moment you start
speaking it comes back to full strength and back to the front, and shows a live level meter and the
elapsed time.

It carries a small toolbar: **Settings**, **History**, **Copy last dictation**, **collapse to the mark**,
and **hide between dictations** (the bar still appears while you speak; Appearance turns it back on).
Drag it anywhere by its body — where you put it is remembered across restarts.

**It never takes focus.** The window is `WS_EX_NOACTIVATE`, so Windows delivers your clicks but never
activates it, and it is raised with `SWP_NOACTIVATE` rather than `SetForegroundWindow`. Press a button or
drag it and the caret stays exactly where you left it, so the dictated text still lands there.

Settings has an on/off for resting on screen and six starting positions (each corner, top or bottom
centre) used until you drag it somewhere. `Overlay.RestingOpacity` and `Overlay.Margin` in
`settings.json` tune how faint it rests and how far it sits from the edge.

## History

Every dictation is logged — what the recogniser heard, what was actually typed, which engine, how long
it took. Open it from the tray (**History…**) or start the app with `--history`.

It exists for the case where you dictate into a window that was not focused, or clicked away mid-
sentence, and the text went nowhere. **Copy last dictation** is the top button; click any older entry to
see it in full and copy that one instead. Tick **Always on top** and the window stays where you left it,
across restarts.

The log lives in `%LOCALAPPDATA%\Talk2Me\history.jsonl`, one JSON object per line, capped at 200 entries
by default. That means everything you dictate is on disk in plain text — the History section in Settings
turns it off, changes the cap, or clears it.

## Managing downloaded models

Settings lists every downloaded model with its size, marks the one your current settings would load, and
lets you tick the ones to remove — **Delete selected**, or **Delete all**. Both engines are unloaded
first so nothing is still mapped. The tray menu keeps a delete-everything shortcut.

## AI cleanup

Off by default. The regex cleaner strips "um" and fixes spacing; it cannot tell that "the deadline is
Monday, no wait, make that Tuesday" should come out as "The deadline is Tuesday." That needs a model.

Turn it on under **AI cleanup** in Settings and paste an Anthropic API key. The key is encrypted with
DPAPI under your Windows account in `%LOCALAPPDATA%\Talk2Mepikey.dat`, never in `settings.json`;
`ANTHROPIC_API_KEY` works too. What it does:

- applies spoken self-corrections and drops the correcting
- obeys spoken formatting — "new line", "new paragraph", "bullet point", "question mark"
- turns a spoken list into a real one
- fixes misheard names and jargon from your personal dictionary
- follows a style (Verbatim / Natural / Formal / Casual) and any extra rules you write

What it never does: answer or act on what you dictated. The transcript is fenced and the prompt is
explicit that it is speech to be typed, not an instruction; a reply that is far longer than the
transcript is discarded on the assumption the model answered it anyway.

When it is on, the transcript text — not the audio — is sent to the Anthropic API. If the call is slow
(2 s by default), fails, or you have no key, the regex-cleaned text is typed instead, so a dead network
degrades dictation rather than breaking it.

Try a rewrite without dictating:

```bash
dotnet run --project tools/Talk2Me.Clean -- "um so the deadline is monday no wait make that tuesday"
```

Arguments: `<transcript> [style] [model] [timeoutMs]`. Prints the raw text, the regex result, the
rewrite, and how long the call took.

## Benchmark a WAV

```bash
dotnet run --project tools/Talk2Me.Bench -- path\to\speech.wav Both 5
```

Arguments: `<wav> [engine=Both|Parakeet|Whisper] [runs] [language] [whisperModel]`. Prints warm-up time,
per-run latency, the transcript from each engine, and a side-by-side summary.

## Tests

```bash
dotnet test
```

## Layout

```
src/Talk2Me.Core            pipeline, abstractions, settings   (no Windows dependencies, fully unit-tested)
src/Talk2Me.Windows         keyboard hook, WaveIn capture, SendInput / clipboard injection
src/Talk2Me.Transcription   Parakeet + Whisper transcribers, router, model download
src/Talk2Me.Llm             Claude-backed rewrite behind ILlmClient (a local model can slot in beside it)
src/Talk2Me.App             WPF tray app, overlay, settings (namespace Talk2Me.Desktop)
tools/Talk2Me.Bench         console harness for latency / backend checks
tools/Talk2Me.Clean         console harness for the LLM cleanup pass
tools/Talk2Me.Brand         renders the icon (.ico) and logo PNGs from the vector mark
branding/                   BRAND.md, SVG sources, exported PNGs
tests/Talk2Me.Core.Tests    xUnit
docs/ARCHITECTURE.md       design notes and roadmap
docs/HANDOFF.md            start here if you are picking the project up
```
