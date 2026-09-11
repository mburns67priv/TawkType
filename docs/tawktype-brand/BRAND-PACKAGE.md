# TawkType brand package

Proposed direction · September 11, 2026

This is a design package for the proposed name. It does not rename the application or replace its existing branding. The naming search found no obvious matching dictation product; domain, app-store, and trademark availability have not been established.

## Brand idea

**TawkType — You talk. It types.**

Push-to-talk dictation for Windows that turns speech into text where you are working. The personality is friendly, capable, and slightly playful. The unusual spelling belongs in the name; the rest of the experience should be easy to read.
we arrived
The promise is less typing and less interruption. Local transcription supports that promise through control and independence. Avoid positioning the product as a general voice assistant.

### Positioning statement

For people who think faster than they type, TawkType is a Windows dictation app that puts spoken words into the app they are using. Speech recognition runs on their computer, with an optional Claude pass for rewriting.

### Three messages

1. **Stay where you work.** Dictate into your current application.
2. **Keep transcription local.** Speech recognition runs on your PC.
3. **Choose the cleanup.** Use local cleanup, or explicitly enable Claude rewriting.

These describe the intended experience. Marketing compatibility claims must reflect tested applications, and privacy claims must reflect actual logging and retention behavior.

## Name

Write **TawkType**, one word, capital T twice. Pronounce it “talk type.”

Use “TawkType for Windows” when platform context matters. Use the full name in the tray tooltip, installer, settings title, release notes, and support materials.

Avoid Tawk Type, Tawktype, TAWKTYPE in prose, TT as a public abbreviation, and TawkType AI. Lowercase `tawktype` is suitable for technical identifiers if the rename is approved.

## Taglines and copy

**Primary:** You talk. It types.

**Instructional:** Hold a key. Speak. Release.

**Secondary:** Your voice does the typing.

**Short description:** Local voice typing for Windows.

**Long description:** Hold a shortcut, speak, and release. TawkType transcribes your speech on your PC and types the result into your current app. Enable an optional Claude rewrite when you want additional cleanup.

Do not use “works everywhere,” “perfect accuracy,” “zero latency,” “nothing is stored,” or “nothing ever leaves your device.” Compatibility varies, history can be retained, and Claude rewriting sends text to an external service.

## Logo direction: speech becomes text

The proposed mark combines a speech bubble, three voice bars, and an I-beam text cursor. Its shape communicates input and output without a microphone illustration. A violet rounded tile provides a recognizable app icon.

Use a solid fill as the default. The mark should remain recognizable without a gradient or animation. This is an original geometric concept, not a variation of the Wispr wordmark.

### Included vector concepts

- `icon.svg`: full-color app icon concept.
- `mark-mono.svg`: monochrome mark with transparent cutouts.
- `wordmark.svg`: dark-background lockup with editable text.
- `brand-board.svg`: visual overview with colors and sample overlay states.

The wordmark uses live system-font text. Before distribution, check its rendering and export an outlined version so the geometry is independent of installed fonts.

### Usage rules

- Keep clear space of at least one quarter of the icon width around standalone artwork.
- Use the wordmark at 24 device-independent pixels or larger; use the icon alone when smaller.
- Keep the mark upright. Do not stretch, rotate, add a bevel, or put it inside another speech bubble.
- Use a white mark on dark backgrounds and a dark mark on light backgrounds.
- In the wordmark, Tawk is neutral and Type may use the violet tint. Single-color wordmarks are valid.
- Do not make the I-beam blink in a static logo.

### Tray and small-size variant

The full mark is a concept for larger artwork. At 16–20 pixels, use a simplified bubble with a single vertical cursor; omit the three voice bars and cursor caps if they merge. Create separately pixel-adjusted 16, 20, 24, and 32 pixel variants and inspect them on both taskbar themes. Do not rely solely on shrinking the 512 pixel icon.

## Palette

| Token | Hex | Role |
|---|---|---|
| Background | `#11131D` | Main dark canvas |
| Surface | `#1C2030` | Cards and overlay |
| Border | `#363C50` | Decorative separators |
| Text | `#F5F6FA` | Main text |
| Secondary text | `#B5BDCF` | Hints and secondary copy |
| Tawk violet | `#7867FF` | Icon tile, primary accent |
| Violet tint | `#B4A9FF` | Accent text on dark |
| Speak coral | `#FF8A9E` | Recording indicator |
| Ready mint | `#69D9B0` | Success indicator |
| Attention amber | `#FFD078` | Recoverable problems |
| Light canvas | `#F6F7FB` | Light-theme background |
| Light surface | `#FFFFFF` | Light-theme cards |
| Light text | `#171925` | Text on light backgrounds |
| Light accent | `#5740CC` | Links and primary controls on light |

Use dark text on bright violet primary buttons unless a measured alternate pairing meets contrast requirements. Coral, mint, and amber are primarily dark-theme accents; do not assume they are readable text on white.

Always pair a status color with a word or symbol. Check text contrast, keyboard-focus contrast, high-contrast mode, and disabled states in the implemented UI. The palette is not an accessibility certification.

## Typography

Use **Segoe UI** as the baseline Windows typeface. Where available, Segoe UI Variable is an optional enhancement with a Segoe UI fallback.

- Wordmark: Bold, default tracking.
- Window title: 24–28 DIP, Semibold.
- Section title: 18 DIP, Semibold.
- Body and controls: 14 DIP, Regular.
- Supporting copy: 12–13 DIP.
- Overlay status: 14 DIP, Semibold.
- Shortcut labels: 12–13 DIP, Semibold.

Use sentence case for interface labels. Avoid wide letter spacing in functional copy. Support Windows scaling instead of baking text into images.

## Voice and tone

Be clear, warm, and matter-of-fact. Use a little humor in introductory or promotional copy, never when handling lost text, microphone failure, or privacy.

Good:
- “Give your keyboard a breather.”
- “Your voice does the typing.”
- “Text copied. Paste when you’re ready.”
- “Couldn’t insert the text. You can still copy it.”

Avoid:
- “Oopsie! Your words went missing.”
- “Unleash your voice with revolutionary AI.”
- “We’re listening” when the microphone is not recording.
- Technical model names in ordinary status messages.

Do not misspell ordinary UI copy to match the name. TawkType is the playful part.

## Overlay and interaction language

| State | Label | Visual |
|---|---|---|
| Ready | “Ready” | Neutral mark; shortcut hint |
| Recording | “Listening…” | Coral dot, waveform, elapsed time |
| Local transcription | “Turning speech into text…” | Violet progress indicator |
| Optional cloud rewrite | “Rewriting with Claude…” | Explicit service name |
| Insertion | “Typing…” | Neutral cursor indicator |
| Delivery complete | “Text inserted” | Mint check |
| Clipboard fallback | “Copied — ready to paste” | Clipboard icon |
| Microphone unavailable | “Microphone unavailable” | Amber alert and actionable detail |
| Failed delivery | “Couldn’t insert text” | Offer copy only if text was retained |

Only report completion after the corresponding action succeeds. Display the selected privacy controls accurately; do not show “History off” as a promise of no disk storage while transcript logging remains.

Animation should acknowledge real state changes: a brief fade, a waveform while recording, and a subtle success transition. Respect reduced-motion preferences. Do not animate an idle waveform.

## Sample launch material

### Website or README hero

**You talk. It types.**

Local voice typing for Windows. Hold a key, speak, and put your words into the app you’re already using.

Primary action: **Download for Windows**

Secondary action: **See how it works**

Supporting line: **Local transcription. Optional Claude rewriting.**

### Three feature cards

**Speak where you work**  
Turn thoughts into messages, documents, and prompts without switching to a separate editor.

**Transcribe on your PC**  
Speech recognition runs locally. Choose whether to keep a transcript history.

**Keep control of cleanup**  
Use local cleanup or enable an optional Claude rewrite.

### Release announcement

Meet TawkType: the new name for Talk2Me. Same push-to-talk dictation, with a name that says what it does. You talk. It types.

Use this announcement only once the rename is actually shipped.

### First-run greeting

**Welcome to TawkType.**

Let’s check your microphone and try your first dictation.

Button: **Get started**

### About panel

**TawkType**  
You talk. It types.

Local dictation for Windows.  
Version [actual version]

Links: Release notes · Report a problem · Privacy

## Production asset checklist

Before shipping the approved direction, produce:

- App ICO with separately checked 16, 20, 24, 32, 48, 64, 128, and 256 pixel representations.
- Light and dark tray variants, including high-contrast treatment.
- Transparent wordmarks for light and dark backgrounds.
- Outlined SVG wordmark and PNG exports at 1× and 2×.
- 512 and 1024 pixel icon PNGs.
- 1200×630 social preview and README banner.
- Screenshots showing actual product behavior.

Do not represent the included vector concepts as finished, tested tray assets.

## Rename rollout

Once the name and design are approved, inventory the visible app name, installer, executable metadata, tray menu, repository description, screenshots, documentation, release notes, and update assets.

Treat update identity and user data paths separately from display branding. Preserve existing settings, models, history preferences, startup behavior, and the installed update channel. Test an upgrade from an existing Talk2Me installation before changing any package identity.

This package deliberately leaves application code, existing branding assets, and distribution configuration untouched.

