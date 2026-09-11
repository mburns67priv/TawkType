# TawkType: competitor feature research and recommendations

Research date: September 11, 2026.

The strongest opportunity is to make TawkType **dependable local dictation that remembers how its user writes**. Better vocabulary, reusable phrases, safe delivery, and easy correction would improve it more than adding another transcription engine.

I researched current official documentation for Wispr Flow, Superwhisper, Aqua Voice, Dragon Professional, and Windows Voice Access. This is a feature comparison, not a hands-on accuracy benchmark; vendor performance claims are not evidence that their recognition is better.

## Status — updated 2026-09-11

The research below is kept as written, with two exceptions: project paths and the product name were
carried across the TawkType rename so its citations still resolve, and its vocabulary example — which
used the app's short-lived previous name — has been changed, because that name belongs to somebody
else and is not written down here any more.

This section records what has since been built, so nobody re-plans something that is finished or
assumes something is finished because the rest is.

| § | Subject | State |
|---|---|---|
| 1 | Vocabulary without Claude | **Done** |
| 2 | Spoken snippets | **Done** |
| 3 | Toggle, cancellation, sounds | **Done** |
| 4 | Dictation box and recovery | **Done** |
| 5 | Modes | **Done**, except per-application defaults |
| 6 | Caret-aware insertion | **Done** |
| 7 | History as a correction tool | **Done** |
| 8 | First-run experience | **Done** |
| 9 | Visible privacy | Not started |

**§1 — vocabulary without Claude.** `PhraseBook` applies preferred spellings and replacements locally,
with or without a key, and runs again after a rewrite so the model cannot undo a correction. The
"remember this replacement" action this section wanted arrived with §7, in the history window.

**§2 — spoken snippets.** Triggered by saying "insert" and the trigger, typed exactly, and never sent
for rewriting. The whole vocabulary imports and exports as a JSON file of its own.

**§3 — toggle, cancellation, sounds.** An optional toggle key, Esc to cancel, optional sounds (off by
default, using the Windows scheme), and a recording limit that *finishes* a runaway dictation rather
than discarding it.

**§4 — dictation box and recovery.** `Recovery` works out what can honestly be offered for a dictation
that did not arrive; the box holds the text editable until the user is done with it and can hand the
foreground back to the window it was aimed at. It never opens itself, exactly as this section asks.
The "preserve the transcript before attempting delivery" requirement was already met by the
`Recognised` event and the in-memory `LastDictation`.

**§5 — modes.** All four, built entirely from local settings: `DictationMode` bundles filler removal,
capitalisation, spacing, caret fitting and a vocabulary of its own; a key cycles them and the bar shows
which is in charge. This section's warning is enforced rather than merely observed — a mode can only
ever *narrow* the permission to use Claude, never grant it, with a test in each direction.
**Per-application defaults are the one part not built**, as this section itself puts them later.
`FocusTarget.ProcessName` is already captured at key-down, so what remains is a map and a settings page.

**§6 — caret-aware insertion.** All five bullets: no doubled spaces, a separating space where one is
needed, no capital dropped into a continuation, a selection distinguished from a caret, and an
unreadable control behaving exactly as it did before. It reads a bounded window through `TextPattern`
at delivery time and revalidates the destination first. The caution was followed too — no screenshots,
no broad application text extraction, no clipboard collection. Sixty-four characters either side of the
caret, read locally and used locally.

**§7 — history as a correction tool.** All seven bullets: search, edit, copy raw or cleaned, re-run
cleanup on the raw transcript, compare before and after, delete single entries, and save a vocabulary
replacement from a correction. Both cautions were followed: cleanup is re-run over *text*, never by
retranscribing audio, and there is no blind-backspace "correct last insertion" anywhere.

**§8 — a first run that ends in a successful dictation.** All six bullets, in a seven-step window:
pick a microphone and watch its level; choose a language and see what its engine will cost to
download; download it *and load it*; record the key and be told about the obvious conflicts; dictate
into a box TawkType owns; and choose history retention and cloud rewriting. The section's real
demand was the gates rather than the steps, and they are what `SetupPlan` is: no step is satisfied by
the user agreeing to it. The microphone gate wants a level above the noise floor, the model gate wants
an engine that initialised, and the last gate wants words back from a real dictation through the real
pipeline. "Ready" means it worked.

**§9 is untouched.** A privacy panel that shows what is actually kept rather than what the settings
imply. First run now *states* it at the end — and states it from what happened — but that is a
sentence at a moment, not the during-use indicator this section asks for.

### Small pieces still open

- **§5's per-application defaults** — process name to mode.
- **Remember… in the dictation box.** It is in the history window; the box is the other place the
  wrong words are already on screen.
- **Seeding the recogniser with the vocabulary.** Whisper's `initial_prompt` takes a word list, so the
  names the user has taught TawkType could be got right before cleanup rather than corrected after.

## What the established apps offer

| App | Features worth studying | Lesson for TawkType |
|---|---|---|
| **Wispr Flow** | Spoken snippets, hands-free recording, contextual formatting, selected-text commands. [Snippets](https://docs.wisprflow.ai/articles/5784437944-create-and-use-snippets), [hands-free](https://docs.wisprflow.ai/articles/6391241694-use-flow-hands-free), [context](https://docs.wisprflow.ai/articles/4678293671-Context-Awareness) | Reduce repetitive work and adapt insertion to where the user is typing. |
| **Superwhisper** | Built-in and custom modes, vocabulary, local/cloud models, and history reprocessing. Its Windows documentation explicitly confirms modes and vocabulary availability. [Windows features](https://superwhisper.com/docs/get-started/windows), [reprocessing](https://superwhisper.com/docs/get-started/transcribe-history) | Let one recording serve different writing tasks, and make failed attempts recoverable. |
| **Aqua Voice** | Custom dictionary, standing instructions, and optional screen context. Its FAQ says transcription requires an internet connection. [Official FAQ](https://aquavoice.com/info/faq) | Personalization matters, but TawkType can implement useful personalization locally. |
| **Dragon Professional** | Vocabulary correction, reusable text, and a Dictation Box for applications without full text control. [Official training](https://support.microsoft.com/en-us/dragon-professional/learn-dragon-professional) | Recognition is only part of the job. Correction and compatibility deserve first-class workflows. |
| **Windows Voice Access** | Offline dictation, correction, spelling, vocabulary, and editing commands. [Official documentation](https://support.microsoft.com/en-us/accessibility/windows/voice-access/dictate-text-with-voice) | Offline operation alone is insufficient differentiation. Everyday control and correction are established expectations. |

These are the improvements I would prioritize for TawkType.

## 1. Make vocabulary useful with Claude turned off

This is the biggest personalization gap. TawkType’s vocabulary currently influences the optional rewrite prompt; a local-only user does not get the corresponding benefit.

Build two separate concepts:

- **Preferred spellings:** names, products, acronyms, and technical terms.
- **Explicit replacements:** “when recognition produces this phrase, replace it with this exact text.”

For example, a user could teach it that “tawk type” should become `TawkType` in a particular vocabulary profile. Matching needs phrase boundaries, language awareness, and controlled casing—not unrestricted substring replacement.

Add a simple correction workflow in history: edit a result, select the mistaken phrase, and choose **Remember this replacement**. Require that explicit choice; silently learning every edit would accumulate bad rules.

Start with deterministic postprocessing. Investigate engine-level vocabulary biasing separately, because support and effectiveness need verification for the actual Parakeet and Whisper integrations.

**Why first:** correcting the same name every day makes an otherwise accurate app feel persistently broken.

## 2. Add local spoken snippets

Wispr’s snippets expand spoken triggers into saved text such as signatures, addresses, links, and reusable replies. They are static expansions rather than dynamic templates. [Wispr documentation](https://docs.wisprflow.ai/articles/5784437944-create-and-use-snippets)

This is an excellent feature for a single developer: useful, understandable, and independent of model quality.

Examples:

| Spoken trigger | Inserted text |
|---|---|
| “Insert my signature” | A saved signature |
| “Insert project link” | An exact repository URL |
| “Insert bug template” | A multiline issue template |
| “Insert support reply” | A frequently used response |

For TawkType:

- Start with an explicit “insert…” trigger convention to reduce accidental expansion.
- Warn about duplicate and overlapping triggers.
- Preserve expansion text exactly, including casing and punctuation.
- Protect expansions from subsequent LLM rewriting.
- Support JSON import/export alongside vocabulary.
- Route multiline expansions through the corrected delivery path identified in the review.

I would postpone variables, scripting, and conditional templates. Static snippets already deliver most of the initial value.

## 3. Add toggle recording, cancellation, and optional sounds

Wispr supports recording without continuously holding a shortcut. [Hands-free documentation](https://docs.wisprflow.ai/articles/6391241694-use-flow-hands-free)

TawkType should offer:

- Hold-to-talk as today.
- A separate toggle shortcut: press to start, press to finish.
- Escape to cancel the active recording or processing operation.
- Optional start, stop, and failure sounds.
- A configurable recording limit and a clear elapsed-time indicator.

This helps with long paragraphs, accessibility, and users who need both hands while speaking.

The prerequisite is explicit session ownership and cancellation in the pipeline. Merely adding a toggle to the keyboard hook would amplify the lifecycle problems from the review.

## 4. Introduce a Dictation Box and reliable recovery

Dragon’s Dictation Box is a useful precedent: provide an editing surface when the destination application cannot reliably support dictation. [Dragon training](https://support.microsoft.com/en-us/dragon-professional/learn-dragon-professional)

For TawkType, I would implement a small scratchpad with:

- The completed transcript.
- Edit, copy, and explicit paste actions.
- A clear explanation when automatic delivery was withheld.
- A shortcut to recover the last result.

Normal successful dictation should remain unobtrusive. The scratchpad becomes available when focus changes, injection fails, or the user explicitly wants to review before inserting.

Crucially, **preserve the transcript before attempting delivery**. An in-memory last-result buffer should work even when persistent history is disabled.

Do not automatically steal focus to show the scratchpad. Notify through the overlay and let the user open it.

## 5. Add a few meaningful modes with quick switching

Superwhisper organizes behavior into modes, including plain transcription and writing-oriented modes. [Mode overview](https://superwhisper.com/docs/get-started/introduction)

TawkType already has some underlying settings, but changing settings is not the same as quickly choosing how the next dictation should behave.

I would start with four modes:

| Mode | Behavior |
|---|---|
| **Literal** | Preserve recognizer output; no filler deletion or rewriting. |
| **Clean prose** | Conservative, language-aware cleanup and normal capitalization. |
| **Chat** | Light formatting, minimal stylistic intervention. |
| **Technical** | Preserve identifiers, acronyms, and exact snippet text; use a technical vocabulary profile. |

“Literal” means preserving what the recognizer produced—not promising verbatim recovery of everything spoken.

Expose the current mode in the overlay and offer a quick keyboard switch. Later, allow per-application defaults based on executable identity.

Keep **mode** and **cloud processing** separate. Choosing “Email” should not silently authorize sending text to Claude.

## 6. Make insertion aware of the caret

Wispr documents contextual behavior such as avoiding unnecessary capitalization in the middle of a sentence. [Context awareness](https://docs.wisprflow.ai/articles/4678293671-Context-Awareness)

TawkType could improve substantially with modest local context:

- Avoid doubled spaces.
- Add a separating space when appropriate.
- Avoid capitalizing a continuation unnecessarily.
- Distinguish replacing selected text from inserting at a caret.
- Treat unsupported controls conservatively.

Start with bounded, best-effort UI Automation reads around the insertion point. Revalidate the destination before delivery, and fall back gracefully when context is unavailable.

I would not start with screenshots, broad application text extraction, or automatic clipboard collection. Superwhisper’s documentation illustrates how much selected text, application context, and clipboard context can enter a rewriting request. That deserves explicit controls. [Context documentation](https://superwhisper.com/docs/common-issues/context)

## 7. Turn history into a correction tool

Superwhisper lets users reprocess past recordings under current mode settings. [History reprocessing](https://superwhisper.com/docs/get-started/transcribe-history)

For TawkType, start with the less expensive, more private version:

- Search history.
- Edit a transcript.
- Copy raw or cleaned text.
- Re-run cleanup on the raw transcript.
- Compare before and after.
- Delete individual entries.
- Save an explicit vocabulary replacement from a correction.

Distinguish **reprocessing text** from **retranscribing audio**. The latter requires retaining audio; I would not introduce a permanent recording archive by default. An optional, short-lived in-memory last recording could support immediate retry, with clear lifetime and size limits.

I would also avoid “correct last insertion” implemented as blind backspaces. Once the user moves the caret or types something else, that becomes destructive.

## 8. Build a first-run experience that ends with a successful dictation

Wispr’s setup guide walks through microphone, shortcut, language, and a first dictation. [Setup guide](https://docs.wisprflow.ai/articles/3152211871-setup-guide)

TawkType should guide a new user through:

1. Select a microphone and see its level.
2. Choose a language and understand the required download.
3. Download and initialize the engine with clear progress.
4. Record a shortcut and check obvious conflicts.
5. Dictate into a built-in test box.
6. Choose history retention and optional cloud rewriting.

“Ready” should mean the model actually initialized and the microphone worked. That is a much stronger onboarding outcome than successfully saving settings.

## 9. Make privacy visible during use

This should be a product feature, not only a settings page.

Show concise state such as:

- **Local transcription**
- **Claude rewrite enabled**
- **History off**

Provide independent controls for transcript retention and external processing. Explain exactly what leaves the device when Claude is enabled, including any future context.

However, these labels are only credible after fixing transcript logging and retention behavior from the review. A “History off” indicator alongside plaintext transcript logs would make the product less trustworthy.

## What I would postpone or reject

- **A general voice assistant that clicks, executes commands, or operates arbitrary apps.** Too much scope and too many failure modes.
- **Meeting recording and diarization.** A different workflow with long recordings, speaker management, and additional privacy expectations.
- **Accounts and cloud synchronization.** Local import/export covers an important initial need.
- **Always-listening wake words.** Poor fit for the current push-to-talk product and privacy proposition.
- **Automatic screen scraping.** Try application defaults and limited caret context first.
- **Live injection of partial transcripts.** Recognition revisions and changing focus make this substantially harder than showing a provisional preview.
- **A large collection of interchangeable engines.** Measure where the existing engines fail before multiplying download, compatibility, and support problems.

Selected-text rewriting is worth considering later. Wispr offers it through Command Mode, but it introduces a second workflow beyond dictation. Start with explicit selection, a separate command, and a preview before replacement. [Command Mode](https://docs.wisprflow.ai/articles/4816967992-how-to-use-command-mode)

## The order I would actually build

First, fix the review’s delivery, privacy, and session-lifecycle failures.

Then ship **local replacements, static snippets, a literal mode, and copy-last recovery**. These are the strongest initial combination of daily value and manageable scope.

Next add **toggle/cancel, guided setup, and the Dictation Box**. Follow with **quick modes, per-app defaults, and caret-aware insertion**.

Judge progress using repeated real tasks: names and jargon, short chat replies, long paragraphs, multiline snippets, focus changes, and unsupported controls. Measure correction effort, lost or misdirected results, and release-to-text latency—not just transcription accuracy.

My first feature investment would be **local vocabulary plus snippets**. My first reliability investment would be **never losing or misdirecting a finished transcript**. Together, those would make TawkType substantially more useful without abandoning its local-first design.
