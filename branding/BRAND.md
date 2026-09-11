# TawkType brand

Local voice typing for Windows. Hold a key, speak, release.

The full design package — positioning, voice, launch copy, production checklist — is
`docs/tawktype-brand/BRAND-PACKAGE.md`. This file is the implementer's half: what the app actually
uses, and where it comes from.

Home: **[tawktype.com](https://tawktype.com)**. Repository:
[JupitorStudioDev/TawkType](https://github.com/JupitorStudioDev/TawkType).

## Name and voice

- **TawkType**. One word, capital T twice. Pronounced "talk type". Never "Tawk Type", "Tawktype",
  "TAWKTYPE" in prose, "TT" as a public abbreviation, or "TawkType AI".
- Lowercase `tawktype` is fine in technical identifiers.
- **Primary tagline**: *You talk. It types.*
- **Instructional**: *Hold a key. Speak. Release.*
- **Short description**: *Local voice typing for Windows.*
- **Tone**: clear, warm, matter-of-fact. Second person, short sentences. A little humour in
  introductory copy, never when handling lost text, a dead microphone, or privacy.
- Do not misspell ordinary interface copy to match the name. TawkType is the playful part.
- Never claim "works everywhere", "perfect accuracy", "zero latency", "nothing is stored", or "nothing
  ever leaves your device". Compatibility varies, history can be retained, and the Claude rewrite
  sends text to an external service.

## The mark

A speech bubble holding three voice bars and an I-beam text cursor: speech going in on the left, text
coming out on the right. The tail is at the bottom-left so it reads as *you* talking, not the computer.
There is deliberately no microphone in it.

- Drawn on a 24-unit grid. Bubble body 2.63…21.38 × 2.63…19.13, corner radius 4.5, tail down to y 22.5.
  Voice bars 1.5 wide at x = 6.75, 9.38, 12. I-beam at x = 15…19.5: two serifs and a stem.
- Source of truth for geometry: `tools/Talk2Me.Brand/Program.cs` and `src/Talk2Me.App/App.xaml`
  (`Brand.BubbleGeometry`, `Brand.BarsGeometry`). `mark.svg` mirrors it for design tools.
- App icon: the mark in white with violet bars on a rounded tile (radius 22.5 %) filled with the Voice
  gradient. Never put the tile on another gradient. Clear space around the tile: 25 % of its width.
- **Below 24 px the mark changes shape.** The three bars and the I-beam's serifs go sub-pixel and merge
  into a smudge, so 16 and 20 px use a separately drawn variant: wider bars, no cursor. That is what
  `SmallestFullMark` in the brand tool selects; do not "fix" it by shrinking the 256 px art.
- Keep it upright. Do not stretch, rotate, bevel, or nest it inside another speech bubble.
- Do not animate the I-beam in a static logo.

## Wordmark

"Tawk" in the text colour, "Type" in the accent — and **the accent depends on the background**. The
pale tint is for dark grounds; on white it barely registers, so the light lockup uses the light accent
instead. A single-colour wordmark is also valid.

Use the wordmark at 24 DIP or larger; below that, use the icon alone. The SVG carries live text, so
export an outlined copy before distributing it.

## Palette

From the design package. Every value below is what the app actually loads.

| Token | Hex | Use |
|---|---|---|
| Background | `#11131D` | Window ground, overlay pill |
| Surface | `#1C2030` | Cards |
| Border | `#363C50` | Borders and separators |
| Text | `#F5F6FA` | Primary text on dark |
| Secondary text | `#B5BDCF` | Hints |
| **Tawk violet** | `#7867FF` | Icon tile, bars in the mark |
| **Violet tint** | `#B4A9FF` | Accent text on dark |
| **Speak coral** | `#FF8A9E` | Recording indicator, danger on dark |
| Voice gradient | violet → coral, 135° | Icon tile, listening meter |
| Ready mint | `#69D9B0` | Delivery succeeded |
| Attention amber | `#FFD078` | Recoverable problems |
| Light canvas | `#F6F7FB` | Light-theme ground |
| Light text | `#171925` | Text on light, logo on light |
| Light accent | `#5740CC` | Accent on light |

Bright violet takes **dark** text, not white. Coral, mint and amber are dark-theme accents; do not
assume they are readable as text on white. Always pair a status colour with a word or a symbol — the
palette is not an accessibility certification, and the contrast of focus rings, disabled states and
high-contrast mode has to be checked in the implemented UI.

## Type

- UI and wordmark: **Segoe UI Variable** (Display for the wordmark, Text for UI), falling back to
  Segoe UI. Ships with Windows, so no licensing or embedding.
- Window title 24–28 DIP Semibold; section title 18 Semibold; body 14 Regular; supporting copy 12–13;
  overlay status 14 Semibold; shortcut labels 12–13 Semibold.
- Sentence case for interface labels. No wide tracking in functional copy. Never bake text into images.

## Overlay wording

| State | Label |
|---|---|
| Resting | the hotkey hint |
| Recording | *Listening…* |
| Local transcription | *Turning speech into text…* |
| Optional cloud rewrite | *Rewriting with Claude…* |
| Insertion | *Typing…* |
| Clipboard fallback | *Copied — ready to paste* |
| Failed delivery | the reason, with the dictation box a click away |

Only report completion after the thing actually succeeded. Name Claude on the one step that leaves the
machine. Do not animate an idle waveform.

## Assets

Run `dotnet run --project tools/Talk2Me.Brand` to regenerate:

- `src/Talk2Me.App/Assets/talk2me.ico` — 16…256 px, used for the exe, windows, and tray. The filename
  keeps the old name; it is referenced by the csproj and by the taskbar identity work.
- `branding/exports/icon-{256,512,1024}.png` — store listings, README.
- `branding/exports/mark-white-512.png`, `mark-violet-512.png` — the glyph alone.
- `branding/exports/logo-on-dark.png`, `logo-on-light.png` — icon + wordmark lockup, transparent.
- `branding/exports/social-1200x630.png` — link preview card.

Vector sources: `branding/mark.svg`, `branding/icon.svg`, `branding/logo.svg`.

**Not yet produced**, from the package's checklist: high-contrast tray variants, outlined SVG
wordmarks, and screenshots showing current behaviour. The generated `.ico` does carry separately
rendered 16/20/24/32/48 px frames.
