<div align="center">

<img src="branding/exports/icon-256.png" width="96" alt="TawkType">

# TawkType

**You talk. It types.** Local voice typing for Windows: hold a key, speak, release, and the text
appears in the app you are already using.

[**tawktype.com**](https://tawktype.com)

<img src="docs/images/bar-listening.png" width="433" alt="The TawkType status bar while listening">

</div>

Speech recognition runs **on your machine**. There is no account, no subscription and no telemetry.
The only thing that can ever leave your computer is the optional Claude rewrite, which is off until you
turn it on and give it a key.

---

## What it does

Hold **Right Ctrl** (or whichever key you pick), talk, let go. A second or two later the text appears
where your cursor is — in your editor, your browser, a chat box, anywhere.

- **Two local engines.** NVIDIA Parakeet TDT 0.6B v3 for English and 24 other European languages, OpenAI
  Whisper `large-v3-turbo` for the rest. TawkType picks per language, or you can force one.
- **Four modes, switched with a key.** *Literal* keeps what the recogniser produced, *Clean prose*
  tidies it, *Chat* stays lower-case and unspaced, *Technical* protects acronyms and your own
  identifiers. All four work with no model involved — picking one never sends anything anywhere.
- **It fits the text to where it lands.** Before typing, it reads the few words either side of your
  cursor: no doubled spaces, a separating space where one is needed, and no capital letter dropped
  into the middle of a sentence. It only ever acts on what it can actually see.
- **It knows where it can type.** Before typing it checks whether the focused element actually accepts
  text, and whether the target window is running as administrator — synthetic keystrokes to an elevated
  window are discarded by Windows without any error. If it can't type, the text goes to your clipboard
  and the bar says *Copied — ready to paste*.
- **Nothing is lost.** Every dictation is kept locally, so one that went into the wrong window is a
  click away. Turn the log off and it means it — nothing keeps a second copy of your words.
- **History that corrects things.** Search what you have said, edit a past dictation, run cleanup over
  it again, delete one entry, or turn a mistake into a permanent correction — TawkType works out which
  words actually changed and offers to remember just those.
- **A dictation box when delivery fails.** If the text could not be typed, it waits in an editable
  scratchpad you can copy from, correct, or send back to the window it was aimed at. It never opens
  itself over what you were doing — the bar tells you, and you open it when you want it.
- **Hold, or toggle.** Hold the key for a sentence; set an optional second key that starts and stops
  with a press each, for long passages or when holding is awkward. **Esc** abandons either — nothing is
  typed, nothing is recorded.
- **Your own words.** Names TawkType should spell your way, corrections for what it keeps mishearing, and
  saved text you insert by saying *"insert"* and a trigger. All of it works on this machine, with or
  without a Claude key, and the whole vocabulary imports and exports as a file of its own.
- **Sounds and a safety net.** Optional start, finish and failure sounds, off by default and using
  your own Windows scheme. A recording limit — five minutes by default — *finishes* a runaway
  dictation rather than throwing it away, so a key left under a book costs you nothing.
- **Optional AI cleanup.** With a Claude API key, dictations are rewritten before typing: spoken
  corrections applied ("no, make that Tuesday"), lists formatted. Your vocabulary is re-applied
  afterwards, so a rewrite can never undo a correction you wrote down. The bar says *Rewriting with
  Claude* while it happens, because that is the one step that leaves your machine.

## Install it

Download **TawkTypeApp-win-Setup.exe** from the
[latest release](https://github.com/JupitorStudioDev/TawkType/releases/latest) and run it. It installs
per-user, needs no administrator rights, and updates itself from that same release feed.

> The installer carries an `App` suffix the application does not. Velopack names it after the package
> id, and that id has to differ from `%LOCALAPPDATA%\TawkType` — the folder your settings and models
> live in — because the installer wipes its own folder on every update.

> **Windows will warn you the first time.** TawkType is not code-signed yet, so SmartScreen shows
> *"Windows protected your PC"*. Choose **More info** → **Run anyway**. That warning is about the
> absence of a paid certificate, not about anything found in the file.

The installer is around 37 MB. **Speech models are not included and are not fetched automatically** —
nothing that large is downloaded without you asking for it. [The first run](#the-first-run) walks you
through choosing one, and through everything else, ending with you dictating a sentence.

Or [run from source](#running-from-source).

## The first run

The first time TawkType starts it opens a short setup, and the last thing it asks you to do is
dictate. Seven steps:

1. **Welcome** — what it does, and what it does with what you say.
2. **Microphone** — pick your input device and watch the level move as you talk.
3. **Model** — choose a language; TawkType says which engine that needs and what it costs to
   download, then fetches it and loads it.
4. **Key** — hold the combination you want to dictate with. It warns you about the obvious clashes:
   the shortcuts nearly every application has, the Windows key, Caps Lock, and a plain key that would
   still type while you held it.
5. **Try it** — hold your key, say a sentence, and watch it appear in a box TawkType owns.
6. **Privacy** — whether to keep a history, and whether to allow the Claude rewrite.
7. **Done** — what is actually true, and an offer to start with Windows.

Each answer is saved as you give it, so there is nothing to confirm at the end and nothing to lose if
you close the window. Nothing is taken on trust either: you cannot move past the microphone step until
the meter has moved, past the model step until the model has *loaded* rather than merely downloaded,
or past the practice step until a dictation has actually produced words. "Ready" means it worked.

**Skip setup** is there if you cannot finish today. **Set up TawkType…** on the tray menu opens it
again whenever you want, as does the `--setup` flag.

## Requirements

- **Windows 10 or 11**, 64-bit
- **A microphone**

|  | Minimum | Comfortable |
|---|---|---|
| CPU | 2 cores | 4+ cores |
| RAM | 4 GB | 8 GB |
| Free disk | ~1 GB | ~3 GB if you keep both models |
| GPU | none needed | 2 GB VRAM, for Whisper |

**A GPU is optional.** Whisper uses Vulkan when a current graphics driver is present and falls back to
the CPU. Parakeet is CPU-only and fast enough there. **No CUDA Toolkit, no Python, no Rust.**

### What Parakeet actually costs

The default engine, and the one those figures are about.

- **640 MB on disk**, and about **850 MB of memory** while it is loaded — that is the whole
  application, not just the model.
- **CPU-only**, using half your logical cores, never fewer than two and never more than eight. On an
  eight-core machine it takes four, so there is headroom for whatever you are dictating into.
- Measured on an i7-11700F: **3.1 s to load** at startup, then **931 ms** to transcribe 13 seconds of
  speech.

Those timings are one desktop. A two-core laptop has fewer threads, slower ones, and less memory
bandwidth, so expect several seconds to load and something nearer real time to transcribe — an
estimate, not a measurement.

### What Whisper costs

The alternative, for the ~75 languages Parakeet does not cover. It behaves quite differently.

- **1.5 GB on disk**, and with a GPU the weights live in **video memory**, not system RAM: measured at
  **~1.5 GB of VRAM**, while the application itself sits at about 330 MB resident.
- **~2.9 s to load** on Vulkan — close to Parakeet's, despite the model being more than twice the size,
  because the GPU is doing the work.
- **Needs roughly 2 GB of free VRAM.** On a machine with no usable GPU it falls back to the CPU, where
  those weights move into system RAM instead — expect around 2 GB, and slower. That fallback is
  reasoned rather than measured.

So the two engines cost different things: Parakeet spends system RAM and CPU, Whisper spends VRAM and
very little else. If you have a graphics card, Whisper is cheaper on the machine than it looks.

Running from source needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0); the
installer needs only the .NET 8 Desktop Runtime, and offers to fetch it.

## Running from source

```bash
git clone https://github.com/JupitorStudioDev/TawkType.git
cd TawkType
dotnet run --project src/TawkType.App
```

TawkType lives in the system tray and on the taskbar, and opens [the first run](#the-first-run) the
same way an installed copy does. Parakeet is about 640 MB, Whisper `large-v3-turbo` about 1.5 GB, and
they go into `%LOCALAPPDATA%\TawkType\models`.

After that, hold Right Ctrl and talk.

## The status bar

<img src="docs/images/bar-resting.png" width="433" alt="The status bar at rest">

It sits on screen, dimmed, showing your hotkey; the moment you speak it comes back to full strength with
a live level meter and a timer. Its toolbar has Settings, History and Copy last dictation, plus a
recovery button that appears only when a dictation failed to land. The active mode's name sits on the
right — a mode you cannot see is a mode you will be surprised by. Drag the bar anywhere; it remembers
where, per monitor.

**It never takes focus.** The window is `WS_EX_NOACTIVATE`, so Windows delivers your clicks but never
activates it. Press a button or drag it and your caret stays exactly where it was.

## Settings

<img src="docs/images/settings-general.png" width="620" alt="The TawkType settings window">

Eight pages, light or dark or following Windows:

| Page | What's there |
|---|---|
| **General** | Overview, your dictation stats, where the files live, deleting your data, and what happens to it if you uninstall |
| **Transcription** | Engine, language, microphone, and managing downloaded models |
| **Activation** | Push-to-talk key, optional toggle key, tap threshold, how text gets inserted, sounds, recording limit |
| **Modes** | The four behaviours, the key that cycles them, and what each one does |
| **Appearance** | Theme, and where the status bar sits |
| **Vocabulary** | Spellings, replacements and snippets — with import and export |
| **AI cleanup** | The optional Claude rewrite |
| **History** | The log's settings; the dictations themselves live in the History window |

Pick a hotkey by clicking the box and **holding the keys you want**, rather than typing their names. A
number that a box cannot use says so underneath, and Save waits until it is fixed — nothing is dropped
silently or clamped to something you did not choose.

## AI cleanup (optional, off by default)

The built-in cleaner strips "um" and fixes spacing. It cannot tell that *"the deadline is Monday, no
wait, make that Tuesday"* should come out as **"The deadline is Tuesday."** That needs a model.

Turn it on in Settings and paste an [Anthropic API key](https://console.anthropic.com/). The key is
encrypted with DPAPI under your Windows account in `apikey.dat` — never in `settings.json`.
`ANTHROPIC_API_KEY` works too.

When it's on, **the transcript** — not the audio — is sent to the Anthropic API. If the call is slow
(2 s by default), fails, or you have no key, the plain cleaned-up text is typed instead, so a dead
network degrades dictation rather than breaking it.

The prompt is explicit that the transcript is speech to be typed, never an instruction. Dictate *"write
me a poem about the sea"* and you get that sentence, not a poem.

## Where your data lives

Everything is under `%LOCALAPPDATA%\TawkType\` — deliberately a different folder from the
application itself, which the installer puts in `%LOCALAPPDATA%\TawkTypeApp\`. The installer wipes its
own folder on every update, so keeping your data out of it is what stops an update erasing your
settings and models:

| File | What |
|---|---|
| `settings.json` | All settings. Plain text |
| `apikey.dat` | Your Anthropic key, DPAPI-encrypted for your Windows account |
| `history.jsonl` | Every dictation. **Plain text** — turn it off in Settings → History if that's not for you, and the file is deleted rather than merely hidden |
| `models\` | Downloaded speech models |
| `logs\tawktype.log` | Rolling 5 MB debug log. Records how long and how many characters, **never the words themselves** |

## Uninstalling the app

Uninstall TawkType the ordinary way: **Settings → Apps → Installed apps**, find *TawkType*, and
choose **Uninstall**. It is a per-user install, so no administrator prompt appears. The Start Menu
entry, the tray icon and the application folder all go.

**Your data does not.** By default an uninstall leaves `%LOCALAPPDATA%\TawkType\` exactly as it is —
your models, your history, your vocabulary, your settings and your encrypted key. That is deliberate:
a speech model is up to 1.5 GB to download again, and your vocabulary is your own work. Reinstall
later and TawkType picks up where you left off, with no download and nothing to set up a second time.

If you would rather it all went, you have two ways to do it, and which one you want depends on
whether you are leaving or tidying.

### Take it with the uninstall

Before uninstalling, open **Settings → General** and tick **Delete all of this if I uninstall
TawkType**. Then uninstall normally. The data folder is removed as part of it, with nothing further to
confirm.

It has to be decided in advance rather than during the uninstall, and that is not an oversight: the
installer's hooks are not permitted to put a question on screen, and are stopped if they take longer
than half a minute. Asking beforehand is the only way the question can honestly be asked at all — so
if you skip this step and uninstall, your data stays behind.

### Delete it now, without uninstalling

**Settings → General → Delete my data…** removes the same things immediately, while TawkType keeps
running. Use it when you want a clean slate, when you are handing the machine on, or when you meant
to tick the box above and are about to uninstall.

Before it deletes anything it shows you the total size and names what is about to go — how many
models, how many dictations in the history, whether a key is stored, and the exact folder. Nothing
happens until you say yes, and it cannot be undone.

Afterwards TawkType still works. It simply starts again from nothing: the next dictation needs a model,
so it has to be downloaded a second time, and the first run offers to walk you through it again.

### By hand

If TawkType is already gone and you want to be sure, delete `%LOCALAPPDATA%\TawkType` in File
Explorer — paste that path into the address bar.

Nothing outside that folder belongs to TawkType except one registry value: `TawkType` under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written only if you turned on *Start TawkType
when I sign in to Windows*. TawkType removes it during the uninstall, whatever you chose about your
data — an entry pointing at a deleted application is nobody's idea of a feature. If you want to
check, it is the row named *TawkType* in **Task Manager → Startup apps**.

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
dotnet test                                              # 385 unit tests, ~2 s
dotnet run --project tools/TawkType.Bench -- speech.wav Both 5
dotnet run --project tools/TawkType.Clean -- "um the deadline is monday no wait tuesday"
dotnet run --project tools/TawkType.Focus -- 15           # what the focus probe sees
dotnet run --project tools/TawkType.Brand                 # regenerate the icon and logos
```

Launch flags: `--settings`, `--history`, `--dictation-box`, `--setup`, `--overlay-demo`.

The version comes from the last release tag — `git describe` — so a local build reports the release it
descends from rather than a number someone forgot to bump. `-p:Version` still wins where it matters.

To build an installer locally:

```bash
./build/pack.ps1 -Version 0.2.9
```

Releases are cut by tagging: `git tag v0.2.9 && git push origin v0.2.9` runs
`.github/workflows/release.yml`, which tests, packs and publishes the GitHub Release that installed
copies update from.

`docs/ARCHITECTURE.md` explains the design and `docs/HANDOFF.md` is the working notes, including the
gotchas that cost the most time. `docs/REVIEW-2026-09-11.md` and `docs/FEATURE-RESEARCH-2026-09-11.md`
are point-in-time assessments, each carrying a note of what has been addressed since.

## Credits

TawkType leans on other people's work:

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

**Source available, not open source.** See [LICENSE.md](LICENSE.md).

Use TawkType for anything you like — personally, at work, in a business of any size — free, with no
limit on machines. Read the source, and build it for your own use: it is published so the privacy
claims above can be checked rather than taken on trust.

What the licence does not give you is the right to redistribute TawkType or to build something else
out of it: no copies, no modified versions, no porting it, no taking parts of the source into another
project. If you want to do something it does not allow, open an issue and ask.

[tawktype.com](https://tawktype.com) · built by [Jupitor Studio](https://github.com/JupitorStudioDev).
