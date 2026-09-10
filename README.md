<div align="center">

<img src="branding/exports/icon-256.png" width="96" alt="Talk2Me">

# Talk2Me

**Push-to-talk dictation for Windows.** Hold a key, speak, release — cleaned-up text is typed
into whatever you were working in.

<img src="docs/images/bar-listening.png" width="433" alt="The Talk2Me status bar while listening">

</div>

Speech recognition runs **on your machine**. There is no account, no subscription and no telemetry.
The only thing that can ever leave your computer is the optional Claude rewrite, which is off until you
turn it on and give it a key.

---

## What it does

Hold **Right Ctrl** (or whichever key you pick), talk, let go. A second or two later the text appears
where your cursor is — in your editor, your browser, a chat box, anywhere.

- **Two local engines.** NVIDIA Parakeet TDT 0.6B v3 for English and 24 other European languages, OpenAI
  Whisper `large-v3-turbo` for the rest. Talk2Me picks per language, or you can force one.
- **It knows where it can type.** Before typing it checks whether the focused element actually accepts
  text, and whether the target window is running as administrator — synthetic keystrokes to an elevated
  window are discarded by Windows without any error. If it can't type, the text goes to your clipboard
  and the bar says *Copied instead*.
- **Nothing is lost.** Every dictation is logged locally, so a dictation that went into the wrong window
  is one click away.
- **Optional AI cleanup.** With a Claude API key, dictations are rewritten before typing: spoken
  corrections applied ("no, make that Tuesday"), lists formatted, your own vocabulary spelled right.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A microphone

A GPU is optional. Whisper uses CUDA 12 if the toolkit is installed, otherwise Vulkan, otherwise the
CPU. Parakeet runs on the CPU and is fast enough there. **No CUDA Toolkit, no Python, no Rust.**

## Running it

```bash
git clone https://github.com/JupitorStudioDev/Talk2Me.git
cd Talk2Me
dotnet run --project src/Talk2Me.App
```

Talk2Me lives in the system tray and on the taskbar. On first run it downloads the active engine's model
— Parakeet is 640 MB, Whisper `large-v3-turbo` is 1.6 GB — into `%LOCALAPPDATA%\Talk2Me\models`. The
status bar shows the download progress.

Then hold Right Ctrl and talk.

## The status bar

<img src="docs/images/bar-resting.png" width="433" alt="The status bar at rest">

It sits on screen, dimmed, showing your hotkey; the moment you speak it comes back to full strength with
a live level meter and a timer. Its toolbar has Settings, History, Copy last dictation, minimise, and
hide-between-dictations. Drag it anywhere — it remembers where, per monitor.

**It never takes focus.** The window is `WS_EX_NOACTIVATE`, so Windows delivers your clicks but never
activates it. Press a button or drag it and your caret stays exactly where it was.

## Settings

<img src="docs/images/settings-general.png" width="620" alt="The Talk2Me settings window">

Six pages, light or dark or following Windows:

| Page | What's there |
|---|---|
| **General** | Overview, your dictation stats, where the files live |
| **Transcription** | Engine, language, microphone, and managing downloaded models |
| **Activation** | Hotkey, tap threshold, how text gets inserted |
| **Appearance** | Theme, and where the status bar sits |
| **AI cleanup** | The optional Claude rewrite |
| **History** | Everything you've dictated, and the log's settings |

## AI cleanup (optional, off by default)

The built-in cleaner strips "um" and fixes spacing. It cannot tell that *"the deadline is Monday, no
wait, make that Tuesday"* should come out as **"The deadline is Tuesday."** That needs a model.

Turn it on in Settings and paste an [Anthropic API key](https://console.anthropic.com/). The key is
encrypted with DPAPI under your Windows account in `%LOCALAPPDATA%\Talk2Me\apikey.dat` — never in
`settings.json`. `ANTHROPIC_API_KEY` works too.

When it's on, **the transcript** — not the audio — is sent to the Anthropic API. If the call is slow
(2 s by default), fails, or you have no key, the plain cleaned-up text is typed instead, so a dead
network degrades dictation rather than breaking it.

The prompt is explicit that the transcript is speech to be typed, never an instruction. Dictate *"write
me a poem about the sea"* and you get that sentence, not a poem.

## Where your data lives

Everything is under `%LOCALAPPDATA%\Talk2Me\`:

| File | What |
|---|---|
| `settings.json` | All settings. Plain text |
| `apikey.dat` | Your Anthropic key, DPAPI-encrypted for your Windows account |
| `history.jsonl` | Every dictation. **Plain text** — turn it off in Settings → History if that's not for you |
| `models\` | Downloaded speech models |
| `logs\talk2me.log` | Rolling 5 MB debug log |

## Performance

Measured on an i7-11700F with an RTX 4060 Ti, same 13-second clip, best of 5:

| Engine | Runs on | Load | Transcribe |
|---|---|---|---|
| Parakeet TDT 0.6B v3 (int8) | CPU, 8 threads | 3.9 s | 931 ms |
| Whisper large-v3-turbo | GPU (Vulkan) | 2.4 s | 413 ms |

Both transcripts were identical and correctly punctuated. Typical end-to-end: 3.2 s of speech typed in
215 ms.

## Developer tools

```bash
dotnet test                                              # 98 unit tests, < 1 s
dotnet run --project tools/Talk2Me.Bench -- speech.wav Both 5
dotnet run --project tools/Talk2Me.Clean -- "um the deadline is monday no wait tuesday"
dotnet run --project tools/Talk2Me.Focus -- 15           # what the focus probe sees
dotnet run --project tools/Talk2Me.Brand                 # regenerate the icon and logos
```

Launch flags: `--settings`, `--history`, `--overlay-demo`.

`docs/ARCHITECTURE.md` explains the design; `docs/HANDOFF.md` is the working notes, including the
gotchas that cost the most time.

## Credits

Talk2Me leans on other people's work:

- **[NVIDIA Parakeet TDT 0.6B v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3)** — © NVIDIA
  Corporation, used unmodified under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), via the
  [ONNX int8 export](https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8) by
  csukuangfj
- **[OpenAI Whisper](https://huggingface.co/openai/whisper-large-v3-turbo)** — MIT
- **[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)** (Apache-2.0) and
  **[Whisper.net](https://github.com/sandrohanea/whisper.net)** (MIT) run them
- **[Wispr Flow](https://wisprflow.ai)** is the product this is modelled on

Full list, with licences: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence

**None yet — all rights reserved.** You're welcome to read the source. You do not currently have
permission to use, copy, modify or distribute it. If you'd like to, open an issue and ask.

Built by [Jupitor Studio](https://github.com/JupitorStudioDev).
