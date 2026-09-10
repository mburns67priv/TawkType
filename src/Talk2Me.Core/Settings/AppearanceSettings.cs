namespace Talk2Me.Core.Settings;

public enum AppTheme
{
    /// <summary>Follow the Windows app theme, and keep following it if the user changes it.</summary>
    System,

    Light,

    Dark,
}

/// <summary>How the windows look. The overlay pill is deliberately not covered here — it floats over
/// other applications and stays dark in every theme so it reads against whatever is behind it.</summary>
public sealed class AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();
}
