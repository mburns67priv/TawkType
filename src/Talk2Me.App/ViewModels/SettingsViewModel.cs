using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;
using Talk2Me.Desktop.Services;
using Talk2Me.Transcription;
using Talk2Me.Windows.Audio;
using Talk2Me.Windows.Input;

namespace Talk2Me.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ModelMaintenance _models;
    private readonly IApiKeyStore _apiKeys;
    private readonly IDictationHistory _history;

    /// <summary>Set by the password box as the user types. Null means "leave the stored key alone".</summary>
    private string? _pendingApiKey;

    [ObservableProperty]
    private Talk2MeSettings _draft;

    [ObservableProperty]
    private string _minimumHoldText;

    [ObservableProperty]
    private string _selectedInputDevice;

    [ObservableProperty]
    private string _modelStorageText;

    [ObservableProperty]
    private string _cleanupTimeoutText;

    [ObservableProperty]
    private string _vocabularyText;

    [ObservableProperty]
    private string _apiKeyStatus;

    [ObservableProperty]
    private string _historyMaxEntriesText;

    [ObservableProperty]
    private string _historyStatus;

    public SettingsViewModel(
        SettingsStore store,
        ModelMaintenance models,
        IApiKeyStore apiKeys,
        IDictationHistory history)
    {
        _store = store;
        _models = models;
        _apiKeys = apiKeys;
        _history = history;
        _draft = store.Current.Clone();
        _minimumHoldText = _draft.MinimumHoldMs.ToString();
        InputDevices = new[] { "(system default)" }.Concat(WaveInAudioCapture.ListInputDevices()).ToArray();
        _selectedInputDevice = Draft.InputDeviceName ?? InputDevices[0];
        _modelStorageText = models.Describe();
        RefreshModels();
        _cleanupTimeoutText = _draft.Cleanup.TimeoutMs.ToString();
        _vocabularyText = string.Join(", ", _draft.Cleanup.Vocabulary);
        _apiKeyStatus = DescribeApiKey();
        _historyMaxEntriesText = _draft.History.MaxEntries.ToString();
        _historyStatus = DescribeHistory();
    }

    public event EventHandler? Saved;

    public IReadOnlyList<string> Hotkeys { get; } = VirtualKeys.Names;

    public IReadOnlyList<TranscriptionEngine> Engines { get; } = Enum.GetValues<TranscriptionEngine>();

    public IReadOnlyList<string> Models { get; } = ModelManager.ModelNames;

    public IReadOnlyList<TextInjectionMode> InjectionModes { get; } = Enum.GetValues<TextInjectionMode>();

    public IReadOnlyList<string> InputDevices { get; }

    public ObservableCollection<ModelListItem> InstalledModels { get; } = new();

    public IReadOnlyList<OverlayPosition> OverlayPositions { get; } = Enum.GetValues<OverlayPosition>();

    public IReadOnlyList<CleanupStyle> CleanupStyles { get; } = Enum.GetValues<CleanupStyle>();

    /// <summary>Suggestions only; the combo is editable so any model id can be typed.</summary>
    public IReadOnlyList<string> CleanupModels { get; } = ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5"];

    public string SettingsPath => _store.Path;

    /// <summary>Called by the password box, which cannot be data-bound.</summary>
    public void SetPendingApiKey(string apiKey) => _pendingApiKey = apiKey;

    [RelayCommand]
    private void ClearApiKey()
    {
        _pendingApiKey = null;
        _apiKeys.Write(null);
        ApiKeyStatus = DescribeApiKey();
    }

    [RelayCommand]
    private void Save()
    {
        if (int.TryParse(MinimumHoldText, out var hold) && hold >= 0)
        {
            Draft.MinimumHoldMs = hold;
        }

        if (int.TryParse(CleanupTimeoutText, out var timeout) && timeout >= 250)
        {
            Draft.Cleanup.TimeoutMs = timeout;
        }

        if (int.TryParse(HistoryMaxEntriesText, out var maxEntries) && maxEntries >= 1)
        {
            Draft.History.MaxEntries = maxEntries;
        }

        Draft.Cleanup.Model = string.IsNullOrWhiteSpace(Draft.Cleanup.Model)
            ? new CleanupSettings().Model
            : Draft.Cleanup.Model.Trim();

        Draft.Cleanup.Vocabulary = VocabularyText
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (_pendingApiKey is not null)
        {
            _apiKeys.Write(_pendingApiKey);
            _pendingApiKey = null;
            ApiKeyStatus = DescribeApiKey();
        }

        Draft.InputDeviceName = SelectedInputDevice == InputDevices[0] ? null : SelectedInputDevice;
        Draft.Language = string.IsNullOrWhiteSpace(Draft.Language) ? "en" : Draft.Language.Trim();

        _store.Save(Draft);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (_history.Recent.Count == 0)
        {
            HistoryStatus = DescribeHistory();
            return;
        }

        var answer = MessageBox.Show(
            $"Delete all {_history.Recent.Count} entries from the dictation history?",
            "Talk2Me",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            _history.Clear();
        }

        HistoryStatus = DescribeHistory();
    }

    private string DescribeHistory()
    {
        var count = _history.Recent.Count;
        return count == 0
            ? $"Nothing logged yet. Kept in {_history.Path}"
            : $"{count} dictation{(count == 1 ? string.Empty : "s")} logged in {_history.Path}";
    }

    private string DescribeApiKey()
    {
        if (_apiKeys.HasKey)
        {
            return "A key is stored for your Windows account (encrypted with DPAPI).";
        }

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
            ? "No key stored. Paste one above, or set ANTHROPIC_API_KEY."
            : "Using the ANTHROPIC_API_KEY environment variable.";
    }

    [RelayCommand]
    private async Task DeleteSelectedModelsAsync(Window? owner)
    {
        var selected = InstalledModels.Where(item => item.IsSelected).Select(item => item.Model).ToArray();

        if (await _models.DeleteAsync(selected, owner))
        {
            RefreshModels();
        }
    }

    [RelayCommand]
    private async Task DeleteAllModelsAsync(Window? owner)
    {
        if (await _models.DeleteAllWithConfirmationAsync(owner))
        {
            RefreshModels();
        }
    }

    private void RefreshModels()
    {
        InstalledModels.Clear();
        foreach (var model in _models.List())
        {
            InstalledModels.Add(new ModelListItem(model));
        }

        ModelStorageText = _models.Describe();
    }
}
