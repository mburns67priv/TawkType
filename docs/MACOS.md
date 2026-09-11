# TawkType on macOS

Written 2026-09-11, from the code as it stands. Nothing here has been run on a Mac. Every claim about
what *exists* was checked against the packages in this repository; every claim about what *works* is
marked as unverified.

**Short answer: yes, and the architecture already did most of the hard part — but not the expensive
part.** The whole of `TawkType.Core` ports untouched. The platform layer is seven small interfaces. The
WPF app has to be rewritten, and the permissions and notarization story is bigger than it looks.

## What ports for free

**`src/TawkType.Core`, about 4,100 lines.** It targets plain `net8.0`, references only
`Microsoft.Extensions.Logging.Abstractions`, and contains no `DllImport`, no `System.Windows`, no
`Microsoft.Win32` and no registry access — verified by grep, not by intention. That is the actual
behaviour of the application:

- the dictation state machine, sessions and cancellation
- `Hotkey`, `HotkeyGesture` and `Activation` — hold, toggle, Esc, mode cycling
- `PhraseBook`, `CaretFit`, `CorrectionGuess`, `Delivery`, `BasicTextCleaner`
- modes, settings, migration, `NumberField`
- the history store, `HistoryQuery`, `LastDictation`, `Recovery`

**Both speech engines have macOS natives.** `Whisper.net.Runtime` 1.9.1 ships `macos-arm64` and
`macos-x64`; sherpa-onnx 1.13.5 declares `org.k2fsa.sherpa.onnx.runtime.osx-arm64` and `osx-x64`.
whisper.cpp has a Metal backend, so Apple Silicon has a plausible GPU path where Windows uses Vulkan.
Neither has been run here.

> `TawkType.App.csproj`'s `TrimForeignNatives` target deletes `*.dylib`, `*.metal` and every `osx-*`
> runtime folder after publish. It exists because Whisper.net copies every platform's binaries into a
> Windows build. On a Mac build it would delete exactly the files the app needs, so it has to become
> conditional on the runtime identifier before anything else is attempted.

**The tests, almost.** `tests/TawkType.Core.Tests` targets `net8.0-windows` only because it references
`TawkType.Transcription`, which is itself `net8.0-windows` to stop Whisper.net dragging in 600 MB of
other platforms. Exactly **one** test file, `ModelStorageTests.cs`, uses that reference. Split it out
or multi-target `TawkType.Transcription`, and the other 24 files run on macOS unchanged.

## What has to be reimplemented

`src/TawkType.Windows`, about 1,650 lines. The seams in `TawkType.Core/Abstractions` are already the
porting boundary — this is what that design was for.

| Interface | Windows today | macOS equivalent | Risk |
|---|---|---|---|
| `IPushToTalkHotkey` | `WH_KEYBOARD_LL` hook | `CGEventTap` | **High** — see secure input |
| `ITextInjector` | `SendInput` + `KEYEVENTF_UNICODE` | `CGEventKeyboardSetUnicodeString` | Medium |
| `IFocusProbe` | UI Automation | `AXUIElement`, `AXSelectedTextRange` | **High** — coverage varies |
| `IAudioCapture` | NAudio `WaveIn` | `AVAudioEngine` | Low |
| `IApiKeyStore` | DPAPI | Keychain | Low |
| `IClipboard` | Win32 clipboard | `NSPasteboard` | Low |
| `IWindowActivator` | `SetForegroundWindow` | `NSRunningApplication.activate` | Low |

Plus `WindowsStartup` (Run key → a `LaunchAgent` plist). `MonitorLayout`, `TaskbarIdentity` and
`TitleBarTheme` have no Mac equivalent and are simply not needed — the taskbar work in particular
(gotcha 20) is a Windows shell problem that does not exist there.

`ITranscriber`, `ITextCleaner`, `ILlmClient`, `ISettingsProvider` and `IDictationHistory` need no Mac
implementation at all.

## What has to be rewritten

**The WPF app, about 6,100 lines.** WPF does not run on macOS and will not.

The view models are the good news: `CommunityToolkit.Mvvm` is cross-platform, so `SettingsViewModel`,
`OverlayViewModel`, `HistoryViewModel` and `DictationBoxViewModel` are mostly portable logic. Every
`.xaml` file is not.

Two credible routes:

- **Avalonia.** XAML, one codebase, the closest thing to WPF that runs on macOS. The window structure
  and the view models survive; the markup is rewritten rather than ported. Fastest path to something
  real, and the styling work (`Themes/`, the templated controls) largely transfers in shape.
- **A native SwiftUI shell over the .NET core.** Best result — a real menu-bar extra, a proper
  non-activating `NSPanel` for the overlay — and two UI codebases forever. Worth it only if the Mac
  version is meant to be a first-class product rather than a port.

MAUI is a poor fit: it is built for app windows, not for a tray utility whose main surface is an
always-on-top panel that must never take focus.

## The parts that are not code

These are what actually sink Mac ports, and none of them is solved by writing C#.

- **Accessibility permission.** Required for both `CGEventTap` and the AX APIs. The user grants it by
  hand in System Settings, it cannot be scripted or bundled, and the app is dead until they do.
- **Input Monitoring permission.** A separate grant, with its own prompt.
- **Microphone permission.** A TCC prompt, same as any recording app.
- **Secure input mode.** When a password field has focus, macOS blocks event taps outright — and some
  applications leave secure input enabled after losing focus. Push-to-talk genuinely stops working,
  there is no API to defeat it, and the honest response is to detect it and say so rather than appear
  broken. This is the single biggest behavioural difference from Windows.
- **Notarization.** Apple Developer Program, about $99/year, mandatory in practice. Gatekeeper is far
  stricter than the SmartScreen warning Windows users currently click through.
- **Universal binary** (arm64 + x64), or arm64 only and say so.

Velopack describes itself as cross-platform and ships `netstandard2.0`/`net8.0` libraries, so updates
could keep their present shape. Untested here.

## The order to do it in

**Not yet.** `v0.2.9` is the last release, and modes, caret fitting, the dictation box, history
corrections and the entire rebrand are in nobody's hands. Starting a second platform before the first
has shipped one user-visible release is the wrong order, and it doubles the surface of every bug found
in the meantime.

When it is time, **spike before porting**. A throwaway console app, a day or two, proving the three
risky primitives on real macOS:

1. **Global key-up capture** through a `CGEventTap`, including what happens when secure input is on.
   The convenient macOS shortcut APIs report presses, not releases — the same gap that ruled out
   `RegisterHotKey` and Tauri's global-shortcut plugin on Windows (see `ARCHITECTURE.md`, "Why this
   stack"). Push-to-talk lives or dies on key-*up*.
2. **Unicode injection** into a third-party app that is not the spike itself — a browser, an Electron
   editor, a native Cocoa text view.
3. **Caret context** via `AXSelectedTextRange` in those same apps. `CaretFit` is already written and
   already tested; it needs `Before`, `After` and a selection flag, and degrades to exactly the old
   behaviour when it cannot get them — so a partial answer here is survivable in a way a partial
   answer to (1) is not.

If those three work, the rest is ordinary engineering on a codebase whose seams are in the right
places. If (1) does not, there is no Mac version worth shipping, and it is much cheaper to learn that
in two days than after rewriting the UI.

## What this repository would look like afterwards

```
src/TawkType.Core          unchanged, shared
src/TawkType.Windows       unchanged
src/TawkType.Mac           the seven interfaces, against CoreGraphics / AppKit / AVFoundation
src/TawkType.App           unchanged (WPF, Windows)
src/TawkType.App.Mac       Avalonia, or a Swift shell
src/TawkType.Transcription multi-targeted, or RID-conditional native trimming
```

Nothing above requires changing `TawkType.Core`, which is the point of having written it that way.
