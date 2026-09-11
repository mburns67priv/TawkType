using System.Text.Json.Serialization;

namespace TawkType.Core.Settings;

public enum TextInjectionMode
{
    /// <summary>Type short text with synthetic key events; paste long text through the clipboard.</summary>
    Auto,
    TypeUnicode,
    Paste,
}

public enum TranscriptionEngine
{
    /// <summary>Parakeet for its 25 European languages, Whisper for everything else.</summary>
    Auto,

    /// <summary>NVIDIA Parakeet TDT 0.6B v3 via sherpa-onnx. Fastest and most accurate for English.</summary>
    Parakeet,

    /// <summary>OpenAI Whisper via whisper.cpp. 99 languages.</summary>
    Whisper,
}

/// <summary>User-editable settings. Persisted as JSON by <see cref="SettingsStore"/>.</summary>
public sealed class TawkTypeSettings
{
    public TranscriptionEngine Engine { get; set; } = TranscriptionEngine.Auto;

    /// <summary>Key name understood by the platform hotkey layer, e.g. "RightControl", "F9", "CapsLock" or a hex VK "0xA3".</summary>
    /// <summary>The canonical form; older files saying "RightControl" still parse to the same key.</summary>
    public string Hotkey { get; set; } = "Right Ctrl";

    /// <summary>When true the hotkey is swallowed so the focused app never sees it. Leave off for modifier keys.</summary>
    public bool SuppressHotkey { get; set; }

    /// <summary>
    /// Optional second combination that starts and stops a dictation with separate presses, rather
    /// than being held. Empty means off. For long passages, and for anyone who cannot comfortably
    /// hold a key while speaking.
    /// </summary>
    public string ToggleHotkey { get; set; } = string.Empty;

    /// <summary>ISO 639-1 code, or "auto".</summary>
    public string Language { get; set; } = "en";

    /// <summary>Whisper ggml model name, e.g. "LargeV3Turbo", "Small", "BaseEn".</summary>
    public string Model { get; set; } = "LargeV3Turbo";

    /// <summary>Presses shorter than this are treated as accidental taps and ignored.</summary>
    public int MinimumHoldMs { get; set; } = 250;

    /// <summary>Superseded by the active mode. Kept so an older file's answer can be migrated.</summary>
    public bool RemoveFillerWords { get; set; } = true;

    /// <summary>Append a trailing space so consecutive dictations flow into one sentence.</summary>
    /// <summary>Superseded by the active mode. Kept so an older file's answer can be migrated.</summary>
    public bool AppendTrailingSpace { get; set; } = true;

    /// <summary>Superseded by the active mode. Kept so an older file's answer can be migrated.</summary>
    public bool FitToCaret { get; set; } = true;

    public TextInjectionMode InjectionMode { get; set; } = TextInjectionMode.Auto;

    /// <summary>Substring of the input device's product name. Null = system default microphone.</summary>
    public string? InputDeviceName { get; set; }

    /// <summary>The optional LLM rewrite pass that runs after <see cref="RemoveFillerWords"/>.</summary>
    public CleanupSettings Cleanup { get; set; } = new();

    /// <summary>Local spellings, replacements and snippets. Applied with or without the Claude pass.</summary>
    public VocabularySettings Vocabulary { get; set; } = new();

    /// <summary>
    /// The named behaviours the user can switch between. Seeded with the built-in four; editable, and
    /// a settings file that has lost them all still dictates — see <c>DictationModes.Resolve</c>.
    /// </summary>
    public DictationMode[] Modes { get; set; } = DictationModes.BuiltIn();

    /// <summary>Which of them is in charge right now. Persisted, so a choice survives a restart.</summary>
    public string ActiveMode { get; set; } = DictationModes.CleanProse;

    /// <summary>Cycles through <see cref="Modes"/>. Empty for none, like the toggle key.</summary>
    public string ModeHotkey { get; set; } = string.Empty;

    /// <summary>
    /// A short sound when a dictation starts, finishes, and when one fails. Off by default: a sound on
    /// every dictation is a lot of sound, and it is only worth it for people who cannot watch the bar.
    /// </summary>
    public bool PlaySounds { get; set; }

    /// <summary>
    /// Finish a dictation automatically after this many seconds, so a key held by a book or a toggle
    /// left on does not record all afternoon. What was said up to that point is still delivered.
    /// Zero means no limit.
    /// </summary>
    public int MaxRecordingSeconds { get; set; } = 300;

    /// <summary>The dictation log and its always-on-screen window.</summary>
    public HistorySettings History { get; set; } = new();

    /// <summary>The floating status pill.</summary>
    public OverlaySettings Overlay { get; set; } = new();

    /// <summary>Window theme.</summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>
    /// True once the first-run flow has been finished or skipped.
    ///
    /// Nullable so that a settings file written before this existed can be told apart from a fresh
    /// one. A file that is already on disk means TawkType has been used and configured by hand, so
    /// <see cref="SettingsStore.Migrate"/> reads null as "done" — onboarding is for a machine that has
    /// never run it, not for everybody who updates.
    /// </summary>
    public bool? SetupCompleted { get; set; }

    /// <summary>First run has not been through. Drives whether the setup window opens at start.</summary>
    [JsonIgnore]
    public bool NeedsSetup => SetupCompleted != true;

    /// <summary>
    /// The mode in charge. Never null, so no caller has to decide what to do about a settings file
    /// naming a mode that has since been deleted.
    /// </summary>
    public DictationMode ActiveModeOrDefault() => DictationModes.Resolve(Modes, ActiveMode);

    public TawkTypeSettings Clone()
    {
        var copy = (TawkTypeSettings)MemberwiseClone();
        // MemberwiseClone is shallow; the draft must not share the nested sections.
        copy.Cleanup = Cleanup.Clone();
        copy.Vocabulary = Vocabulary.Clone();
        copy.Modes = Modes.Select(mode => mode.Clone()).ToArray();
        copy.History = History.Clone();
        copy.Overlay = Overlay.Clone();
        copy.Appearance = Appearance.Clone();
        return copy;
    }
}
