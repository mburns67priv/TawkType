namespace Murmur.Core.Settings;

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
public sealed class MurmurSettings
{
    public TranscriptionEngine Engine { get; set; } = TranscriptionEngine.Auto;

    /// <summary>Key name understood by the platform hotkey layer, e.g. "RightControl", "F9", "CapsLock" or a hex VK "0xA3".</summary>
    public string Hotkey { get; set; } = "RightControl";

    /// <summary>When true the hotkey is swallowed so the focused app never sees it. Leave off for modifier keys.</summary>
    public bool SuppressHotkey { get; set; }

    /// <summary>ISO 639-1 code, or "auto".</summary>
    public string Language { get; set; } = "en";

    /// <summary>Whisper ggml model name, e.g. "LargeV3Turbo", "Small", "BaseEn".</summary>
    public string Model { get; set; } = "LargeV3Turbo";

    /// <summary>Presses shorter than this are treated as accidental taps and ignored.</summary>
    public int MinimumHoldMs { get; set; } = 250;

    public bool RemoveFillerWords { get; set; } = true;

    /// <summary>Append a trailing space so consecutive dictations flow into one sentence.</summary>
    public bool AppendTrailingSpace { get; set; } = true;

    public TextInjectionMode InjectionMode { get; set; } = TextInjectionMode.Auto;

    /// <summary>Substring of the input device's product name. Null = system default microphone.</summary>
    public string? InputDeviceName { get; set; }

    public MurmurSettings Clone() => (MurmurSettings)MemberwiseClone();
}
