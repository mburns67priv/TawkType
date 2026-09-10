using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Murmur.Core.Settings;
using Murmur.Desktop.Services;
using Murmur.Transcription;
using Murmur.Windows.Audio;
using Murmur.Windows.Input;

namespace Murmur.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ModelMaintenance _models;

    [ObservableProperty]
    private MurmurSettings _draft;

    [ObservableProperty]
    private string _minimumHoldText;

    [ObservableProperty]
    private string _selectedInputDevice;

    [ObservableProperty]
    private string _modelStorageText;

    public SettingsViewModel(SettingsStore store, ModelMaintenance models)
    {
        _store = store;
        _models = models;
        _draft = store.Current.Clone();
        _minimumHoldText = _draft.MinimumHoldMs.ToString();
        InputDevices = new[] { "(system default)" }.Concat(WaveInAudioCapture.ListInputDevices()).ToArray();
        _selectedInputDevice = Draft.InputDeviceName ?? InputDevices[0];
        _modelStorageText = models.Describe();
    }

    public event EventHandler? Saved;

    public IReadOnlyList<string> Hotkeys { get; } = VirtualKeys.Names;

    public IReadOnlyList<TranscriptionEngine> Engines { get; } = Enum.GetValues<TranscriptionEngine>();

    public IReadOnlyList<string> Models { get; } = ModelManager.ModelNames;

    public IReadOnlyList<TextInjectionMode> InjectionModes { get; } = Enum.GetValues<TextInjectionMode>();

    public IReadOnlyList<string> InputDevices { get; }

    public string SettingsPath => _store.Path;

    [RelayCommand]
    private void Save()
    {
        if (int.TryParse(MinimumHoldText, out var hold) && hold >= 0)
        {
            Draft.MinimumHoldMs = hold;
        }

        Draft.InputDeviceName = SelectedInputDevice == InputDevices[0] ? null : SelectedInputDevice;
        Draft.Language = string.IsNullOrWhiteSpace(Draft.Language) ? "en" : Draft.Language.Trim();

        _store.Save(Draft);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeleteModelsAsync(Window? owner)
    {
        await _models.DeleteAllWithConfirmationAsync(owner);
        ModelStorageText = _models.Describe();
    }
}
