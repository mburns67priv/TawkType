# Talk2Me brand

Push-to-talk dictation for Windows. Hold a key, say it, it's typed.

## Name and voice

- **Talk2Me**. One word, capital T and M, the digit 2 always. Never "Talk to Me", "Talk2me", "T2M" in
  user-facing copy.
- **Tagline**: *Hold. Speak. Done.*
- **Tone**: direct, warm, second person. Short sentences. It talks to you like a good assistant, not a
  product. "Listening…", "Typing…", "Your words, typed." Avoid "AI-powered", "revolutionary", exclamation
  marks.

## The mark

A speech bubble with three sound bars inside: voice becoming a message. Tail at the bottom-left so it
reads as *you* talking, not the computer.

- Drawn on a 24-unit grid. Bubble body 2..22 × 3..17, corner radius 4, tail from (6,17) to (6,21) to
  (10,17). Bars 2 wide, radius 1, at x = 8, 11, 14, heights 4, 8, 6, centred on y = 10.
- Source of truth for geometry: `tools/Talk2Me.Brand/Program.cs` and `src/Talk2Me.App/App.xaml`
  (`Brand.BubbleGeometry`, `Brand.BarsGeometry`). `mark.svg` mirrors it for design tools.
- App icon: the mark in white with violet bars on a rounded tile (radius 22.5 %) filled with the Voice
  gradient. Never put the tile on another gradient. Minimum clear space around the tile: 25 % of its width.
- Single-colour use (tray, monochrome contexts): bubble only, bars cut out, in white or Ink.

## Palette

| Token | Hex | Use |
|---|---|---|
| Ink | `#0E0F16` | App background, logo on light |
| Surface | `#171925` | Cards, overlay pill |
| Surface raised | `#1E2030` | Settings window |
| Line | `#2A2D3E` | Borders |
| Text | `#F5F6FA` | Primary text |
| Text muted | `#9CA1B8` | Hints, secondary |
| **Talk violet** | `#6D5DFF` | Primary accent, bars in the mark, transcribing |
| **Echo coral** | `#FF6A8A` | Secondary accent, the "2", listening |
| Voice gradient | violet → coral, 135° | Icon tile, listening ring, meter |
| Mint | `#34D399` | Success: text typed |
| Amber | `#FBBF24` | Errors, warnings |

State colours in the overlay: listening = coral, transcribing = violet, typing = mint, error = amber.

## Type

- UI and wordmark: **Segoe UI Variable** (Display for the wordmark, Text for UI), falling back to Segoe UI.
  Ships with Windows 11, so no font licensing or embedding.
- Wordmark: "Talk2Me" in Segoe UI Variable Display Bold, with the "2" filled with the Voice gradient (or
  Echo coral where gradients are unavailable). Tracking default.
- Body sizes in-app: 13 px UI, 11.5 px hints, 14 px overlay status.

## Assets

Run `dotnet run --project tools/Talk2Me.Brand` to regenerate:

- `src/Talk2Me.App/Assets/talk2me.ico` — 16…256 px, used for the exe, windows, and tray.
- `branding/exports/icon-{256,512,1024}.png` — store listings, README.
- `branding/exports/mark-white-512.png`, `mark-violet-512.png` — the glyph alone.
- `branding/exports/logo-on-dark.png`, `logo-on-light.png` — icon + wordmark lockup, transparent.
- `branding/exports/social-1200x630.png` — link preview card.

Vector sources: `branding/mark.svg`, `branding/icon.svg`, `branding/logo.svg`.
