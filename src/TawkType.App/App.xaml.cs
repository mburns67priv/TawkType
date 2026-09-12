using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using H.NotifyIcon;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TawkType.Core.Abstractions;
using TawkType.Core.History;
using TawkType.Core.Input;
using TawkType.Core.Models;
using TawkType.Core.Pipeline;
using TawkType.Core.Settings;
using TawkType.Core.Text;
using TawkType.Desktop.Logging;
using TawkType.Desktop.Services;
using TawkType.Desktop.ViewModels;
using TawkType.Desktop.Views;
using TawkType.Llm;
using TawkType.Transcription;
using TawkType.Windows.Audio;
using TawkType.Windows.Injection;
using TawkType.Windows.Input;
using TawkType.Windows.Security;
using TawkType.Windows.Shell;
using TawkType.Windows.Startup;
using Velopack;

namespace TawkType.Desktop;

public partial class App : Application
{
    /// <summary>Held for the life of the process; a second copy sees it and bows out.</summary>
    private static Mutex? _singleInstance;

    private readonly CancellationTokenSource _shutdown = new();
    private IHost? _host;
    private TaskbarIcon? _tray;
    private FileLoggerProvider? _fileLog;

    /// <summary>What the automatic-update setting was last time it was looked at, to spot it being turned on.</summary>
    private bool _checkedAutomatically;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private DictationBoxWindow? _dictationBox;
    private SetupWindow? _setupWindow;

    /// <summary>
    /// The last dictation that did not reach where it was aimed, kept so the box can explain itself
    /// when the user gets round to opening it. Cleared by the next dictation that lands properly.
    /// </summary>
    private DictationCompleted? _undelivered;
    private TaskbarWindow? _taskbarWindow;
    private DictationEngine? _engine;
    private ILogger<App>? _logger;

    private IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // First, before any window exists. Velopack uses these hooks to finish installs, updates and
        // uninstalls, and several of them exit the process rather than carrying on into the app.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(true)
            .OnBeforeUninstallFastCallback(_ => OnUninstall())
            .Run();

        // Reproduces an installed copy's process identity from a plain build, which is the only
        // difference that matters for how the taskbar resolves this app's icon. Without it the two
        // cases can only be compared by packing and installing.
        var testIdentity = Environment.GetEnvironmentVariable("TAWKTYPE_TEST_AUMID");
        if (!string.IsNullOrWhiteSpace(testIdentity))
        {
            TaskbarIdentity.SetProcessAppUserModelId(testIdentity);
        }

        // A global hotkey and a tray icon do not survive being run twice. An installer that also sets
        // start-with-Windows makes a second copy easy to trigger, so this is not theoretical.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TawkType.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                _fileLog = new FileLoggerProvider(
                    Path.Combine(SettingsStore.AppDataDirectory, "logs"),
                    DiagnosticLogWanted());
                logging.AddProvider(_fileLog);
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        // Before any window is created, so nothing renders in the wrong theme first.
        Services.GetRequiredService<ThemeManager>().Apply();

        _logger = Services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("TawkType {Version} starting", typeof(App).Assembly.GetName().Version);

        DispatcherUnhandledException += (_, args) =>
        {
            _logger.LogError(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };

        // A taskbar button for as long as TawkType runs, independent of whether the bar is on screen.
        _taskbarWindow = new TaskbarWindow();
        _taskbarWindow.RestoreRequested += (_, _) => RestoreOverlay();
        _taskbarWindow.QuitRequested += (_, _) => Shutdown();
        _taskbarWindow.Show();
        _logger?.LogInformation("Taskbar icon: {Result}", _taskbarWindow.IconDiagnostics);

        _tray = (TaskbarIcon)FindResource("TrayIcon");

        // Left-clicking the tray icon brings the bar back after the minimise button has put it away.
        _tray.LeftClickCommand = new RelayCommand(RestoreOverlay);
        _tray.ForceCreate();

        var overlayVm = Services.GetRequiredService<OverlayViewModel>();
        overlayVm.SettingsRequested += (_, _) => OnSettingsClick(this, new RoutedEventArgs());
        overlayVm.HistoryRequested += (_, _) => ShowHistoryWindow();
        overlayVm.DictationBoxRequested += (_, _) => ShowDictationBox();
        overlayVm.QuitRequested += (_, _) => Shutdown();
        _overlay = new OverlayWindow(overlayVm, Services.GetRequiredService<ISettingsProvider>());

        _engine = Services.GetRequiredService<DictationEngine>();
        var sounds = Services.GetRequiredService<SoundCues>();
        _engine.StateChanged += (_, state) => Dispatcher.BeginInvoke(() => overlayVm.ApplyState(state));
        _engine.StateChanged += (_, state) => sounds.ForState(state);
        _engine.AudioLevelChanged += (_, level) => Dispatcher.BeginInvoke(() => overlayVm.PushLevel(level));
        _engine.Failed += (_, ex) => Dispatcher.BeginInvoke(() => overlayVm.ShowError(FriendlyMessage(ex)));
        // The buffer is set from the words themselves, before delivery; the history from the outcome.
        _engine.Recognised += (_, recognised) =>
            Services.GetRequiredService<LastDictation>().Set(ToRecord(recognised));
        _engine.Completed += (_, completed) =>
            Services.GetRequiredService<LastDictation>().Set(ToRecord(completed));
        _engine.Completed += OnDictationCompleted;
        _engine.Completed += (_, completed) =>
        {
            // Remembered, never shown unprompted. The bar says something happened; the box waits until
            // it is asked for, because a window appearing over the user's work is a worse interruption
            // than the delivery that just failed.
            _undelivered = Recovery.For(completed).Needed ? completed : null;
            Dispatcher.BeginInvoke(() => overlayVm.SetRecoverable(_undelivered is not null));

            if (completed.Delivery == DictationDelivery.CopiedToClipboard)
            {
                Dispatcher.BeginInvoke(() => overlayVm.ShowNotice("Copied — ready to paste", completed.Reason));
            }
            else if (completed.Delivery == DictationDelivery.Failed)
            {
                Dispatcher.BeginInvoke(() => overlayVm.ShowError(completed.Reason));
            }
        };
        // Mode switching lives here rather than in the engine: the engine can read settings but not
        // save them, and a mode the user picked has to survive a restart the way any other choice does.
        Services.GetRequiredService<IPushToTalkHotkey>().NextModeRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => SwitchToNextMode(overlayVm));

        overlayVm.SetMode(Services.GetRequiredService<SettingsStore>().Current.ActiveModeOrDefault().Name);

        // A staged update used to be announced nowhere: the check ran in the background, the download
        // finished, and it applied itself whenever the app was next restarted for unrelated reasons.
        // The bar is the only surface that is always on screen, so it is where this belongs.
        var updates = Services.GetRequiredService<UpdateService>();
        overlayVm.UpdateRequested += (_, _) => updates.RestartAndUpdate();
        updates.Changed += (_, _) => Dispatcher.BeginInvoke(() => OnUpdateStateChanged(updates, overlayVm));

        // The tray menu's "Restart to update" item binds against this, and the tray is the only way
        // back when the bar has been hidden.
        _tray.DataContext = overlayVm;

        // The tooltip names the key the user chose, so it is built here rather than written in XAML,
        // and rebuilt whenever they change it.
        RefreshTrayTooltip();
        Services.GetRequiredService<SettingsStore>().Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            RefreshTrayTooltip();

            // Switching the log off takes effect on the next line written, not the next start.
            if (_fileLog is not null)
            {
                _fileLog.Enabled = Services.GetRequiredService<SettingsStore>().Current.WriteDiagnosticLog;
            }

            // Turning automatic checks on is itself the user asking, so it checks there and then
            // rather than leaving them to wonder for up to six hours whether anything happened.
            var checksNow = updates.ChecksAutomatically;
            if (checksNow && !_checkedAutomatically)
            {
                _ = updates.CheckAsync(_shutdown.Token);
            }

            _checkedAutomatically = checksNow;
        });

        _checkedAutomatically = updates.ChecksAutomatically;

        _engine.Start(); // installs the keyboard hook on this (message-pumping) thread

        if (Services.GetRequiredService<SettingsStore>().Current.History.OpenOnStart
            || e.Args.Contains("--history", StringComparer.OrdinalIgnoreCase))
        {
            ShowHistoryWindow();
        }

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            OnSettingsClick(this, new RoutedEventArgs());
        }

        if (e.Args.Contains("--dictation-box", StringComparer.OrdinalIgnoreCase))
        {
            ShowDictationBox();
        }

        // After the engine is started, because the last thing first run asks the user to do is hold the
        // hotkey and dictate — through the running pipeline, not a demonstration of it.
        if (Services.GetRequiredService<SettingsStore>().Current.NeedsSetup
            || e.Args.Contains("--setup", StringComparer.OrdinalIgnoreCase))
        {
            ShowSetup();
        }

        if (e.Args.Contains("--overlay-demo", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunOverlayDemoAsync(overlayVm);
            return;
        }

        _ = WarmUpAsync(overlayVm);
        _ = Services.GetRequiredService<UpdateService>().RunInBackgroundAsync(_shutdown.Token);
    }

    /// <summary>Cycles the overlay through every state so it can be styled without dictating. Start with --overlay-demo.</summary>
    private async Task RunOverlayDemoAsync(OverlayViewModel overlayVm)
    {
        var random = new Random();
        while (!_shutdown.IsCancellationRequested)
        {
            overlayVm.ApplyState(DictationState.Listening);
            for (var i = 0; i < 40; i++)
            {
                overlayVm.PushLevel((float)random.NextDouble());
                await Task.Delay(75);
            }

            overlayVm.ApplyState(DictationState.Transcribing);
            await Task.Delay(1500);
            overlayVm.ApplyState(DictationState.Injecting);
            await Task.Delay(1200);
            overlayVm.ShowError("Example error message");
            await Task.Delay(2500);
            overlayVm.ReportProgress(new ModelProgress("Downloading model", 640, 1000));
            await Task.Delay(2000);
            overlayVm.HideProgress();
            await Task.Delay(1500);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _engine?.Stop();

        if (_historyWindow is not null)
        {
            _historyWindow.AllowClose = true;
            _historyWindow.Close();
        }

        if (_dictationBox is not null)
        {
            _dictationBox.AllowClose = true;
            _dictationBox.Close();
        }

        if (_setupWindow is not null)
        {
            _setupWindow.AllowClose = true;
            _setupWindow.Close();
        }

        if (_taskbarWindow is not null)
        {
            _taskbarWindow.AllowClose = true;
            _taskbarWindow.Close();
        }

        _overlay?.Close();
        _tray?.Dispose();
        _host?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Velopack's uninstall hook. It always removes the start-with-Windows entry, and deletes the data
    /// folder as well if the user said in Settings that it should go.
    ///
    /// Everything about this method is shaped by the contract: Velopack's hooks may show no UI and are
    /// killed after 30 seconds, so there is no prompt here, nothing to await, and no exception that
    /// can escape. It also runs before the host exists, so the settings file is read directly rather
    /// than through DI, and the note goes to %TEMP% — the folder it would otherwise be written to is
    /// the one being deleted.
    ///
    /// Never verified against a real uninstall. An agent session cannot install anything (gotcha 28),
    /// so the first person to uninstall an installed build is the first person to run this.
    /// </summary>
    private static void OnUninstall()
    {
        try
        {
            // Unconditional, and nothing to do with the data question. TawkType writes this value
            // itself, so Velopack knows nothing about it and would leave Windows trying to launch a
            // deleted executable at every sign-in. The path argument is unused when disabling.
            if (WindowsStartup.IsEnabled())
            {
                NoteForUninstall(WindowsStartup.Set(false, string.Empty)
                    ? "removed the start-with-Windows entry"
                    : "could not remove the start-with-Windows entry");
            }

            var store = new SettingsStore(NullLogger<SettingsStore>.Instance);
            if (!store.Current.DeleteDataOnUninstall)
            {
                return;
            }

            AppDataMaintenance.DeleteQuietly(
                AppDataMaintenance.DataDirectory,
                AppDataMaintenance.InstallDirectory,
                NoteForUninstall);
        }
        catch (Exception ex)
        {
            NoteForUninstall("hook failed: " + ex.Message);
        }
    }

    /// <summary>
    /// The only trace this hook leaves. %TEMP% because the log folder is inside what is being deleted,
    /// and because this is the one code path nobody can watch run.
    /// </summary>
    private static void NoteForUninstall(string message)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "TawkType");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "uninstall.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Nothing left to do about it, and an uninstall must not fail over a log line.
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<SettingsStore>());

        services.AddSingleton<IPushToTalkHotkey, PushToTalkHotkey>();
        services.AddSingleton<IAudioCapture, WaveInAudioCapture>();

        services.AddSingleton<ModelManager>();
        services.AddSingleton<WhisperTranscriber>();
        services.AddSingleton<ParakeetModelManager>();
        services.AddSingleton<ParakeetTranscriber>();
        services.AddSingleton<TranscriberRouter>();
        services.AddSingleton<ITranscriber>(sp => sp.GetRequiredService<TranscriberRouter>());
        services.AddSingleton<ModelStorage>();
        services.AddSingleton<ModelMaintenance>();
        services.AddSingleton<AppDataMaintenance>();

        services.AddSingleton<IApiKeyStore, DpapiApiKeyStore>();
        services.AddSingleton<ILlmClient, ClaudeLlmClient>();
        services.AddSingleton<ITextCleaner, LlmTextCleaner>();

        services.AddSingleton<IFocusProbe, UiaFocusProbe>();
        services.AddSingleton<IClipboard, WindowsClipboard>();

        services.AddSingleton<UnicodeTypingInjector>();
        services.AddSingleton<ClipboardPasteInjector>();
        services.AddSingleton<ITextInjector, AutoTextInjector>();

        services.AddSingleton<ThemeManager>();
        services.AddSingleton<SoundCues>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<DictationHistoryStore>();
        services.AddSingleton<IDictationHistory>(sp => sp.GetRequiredService<DictationHistoryStore>());
        services.AddSingleton<LastDictation>();
        services.AddSingleton<IWindowActivator, Win32WindowActivator>();
        services.AddSingleton<DictationBoxViewModel>();

        services.AddSingleton<DictationEngine>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SetupViewModel>();
    }

    /// <summary>
    /// Loads the model and warms the runtime, but only if the model is already on disk. TawkType never
    /// downloads one by itself — Settings → Transcription does that, on the user's say-so — so when
    /// there is nothing to load the bar says so and waits.
    /// </summary>
    private async Task WarmUpAsync(OverlayViewModel overlayVm)
    {
        var transcriber = Services.GetRequiredService<ITranscriber>();
        var progress = new Progress<ModelProgress>(overlayVm.ReportProgress);

        if (!transcriber.IsModelReady)
        {
            _logger?.LogInformation("No model downloaded for the active engine; waiting for the user");
            overlayVm.ModelMissing = true;
            return;
        }

        overlayVm.ModelMissing = false;

        try
        {
            await transcriber.WarmUpAsync(progress, _shutdown.Token);
            overlayVm.HideProgress();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Model warm-up failed");
            overlayVm.ShowError("Model failed to load: " + FriendlyMessage(ex));
        }
    }

    private static string FriendlyMessage(Exception ex)
    {
        // The one error with a specific thing to do about it, so it says the thing rather than the fault.
        if (ex is ModelNotDownloadedException || ex.InnerException is ModelNotDownloadedException)
        {
            return "Download a model in Settings";
        }

        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Length > 90 ? message[..90] + "…" : message;
    }

    /// <summary>Logs the dictation so it can be recovered from the history window.</summary>
    private void OnDictationCompleted(object? sender, DictationCompleted completed)
    {
        try
        {
            Services.GetRequiredService<IDictationHistory>().Add(ToRecord(completed));
        }
        catch (Exception ex)
        {
            // The words are already in the session buffer; a history failure is not worth interrupting.
            _logger?.LogWarning(ex, "Could not record the dictation in the history");
        }
    }

    private DictationRecord ToRecord(DictationCompleted completed) => new()
    {
        RawText = completed.RawText,
        FinalText = completed.CleanText,
        Engine = Services.GetRequiredService<TranscriberRouter>().ActiveEngine.ToString(),
        AudioSeconds = completed.AudioDuration.TotalSeconds,
        TranscriptionMs = (int)completed.TranscriptionTime.TotalMilliseconds,
        Delivery = completed.Delivery,
    };

    private void OnShowOverlayClick(object sender, RoutedEventArgs e) => RestoreOverlay();

    /// <summary>Undoes both the bar's minimise button and its close button.</summary>
    private void RestoreOverlay() => Services.GetRequiredService<OverlayViewModel>().Restore();

    private void OnHistoryClick(object sender, RoutedEventArgs e) => ShowHistoryWindow();

    private void ShowHistoryWindow()
    {
        _historyWindow ??= new HistoryWindow(
            Services.GetRequiredService<HistoryViewModel>(),
            Services.GetRequiredService<SettingsStore>().Current.History);

        _historyWindow.Show();
        _historyWindow.Activate();
    }

    /// <summary>
    /// The mode-cycling key. Saved, not held in memory: a mode nobody can see the state of after a
    /// restart is a worse thing than one extra settings write.
    /// </summary>
    private void SwitchToNextMode(OverlayViewModel overlay)
    {
        try
        {
            var store = Services.GetRequiredService<SettingsStore>();
            var next = DictationModes.Next(store.Current.Modes, store.Current.ActiveMode);

            var updated = store.Current.Clone();
            updated.ActiveMode = next.Name;
            store.Save(updated);

            overlay.SetMode(next.Name);
            overlay.ShowNotice(next.Name);
        }
        catch (Exception ex)
        {
            // Never worth failing a key press over; the mode simply stays as it was.
            _logger?.LogWarning(ex, "Could not switch dictation mode");
        }
    }

    private void OnDictationBoxClick(object sender, RoutedEventArgs e) => ShowDictationBox();

    /// <summary>
    /// Opens the box on whatever is worth recovering. Activating it here is right — the user asked for
    /// it — and is the opposite of the automatic focus theft the box exists to avoid.
    /// </summary>
    private void ShowDictationBox()
    {
        var viewModel = Services.GetRequiredService<DictationBoxViewModel>();

        if (_dictationBox is null)
        {
            // Clearing the box is a decision about the dictation being held for recovery, so the copy
            // the app is keeping goes with it — and the bar stops saying there is something waiting.
            // Leaving either behind would mean an empty box the bar still insists has text in it, and
            // a reopen that brings the words back.
            viewModel.Discarded += (_, _) =>
            {
                _undelivered = null;
                Services.GetRequiredService<OverlayViewModel>().SetRecoverable(false);
            };

            _dictationBox = new DictationBoxWindow(viewModel);
        }

        viewModel.Load(_undelivered);
        _dictationBox.Show();
        _dictationBox.Activate();
    }

    /// <summary>
    /// Whether the diagnostic log is wanted, read straight out of the settings file.
    ///
    /// The logger is built before the DI container, so there is no <c>SettingsStore</c> to ask yet —
    /// and constructing a second one to find out would run the migration, which writes. Reading the one
    /// boolean here is what makes "off" mean that no line is ever written, rather than a handful at
    /// every start before the real setting arrives. Anything unreadable answers yes, because a missing
    /// log is worse than an unwanted one when something has already gone wrong.
    /// </summary>
    private static bool DiagnosticLogWanted()
    {
        try
        {
            if (!File.Exists(SettingsStore.DefaultPath))
            {
                return true;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(SettingsStore.DefaultPath));
            return !document.RootElement.TryGetProperty("WriteDiagnosticLog", out var value)
                || value.ValueKind != JsonValueKind.False;
        }
        catch
        {
            return true;
        }
    }

    private void OnApplyUpdateClick(object sender, RoutedEventArgs e)
        => Services.GetRequiredService<UpdateService>().RestartAndUpdate();

    /// <summary>
    /// Puts a staged update on the bar and in the tray tooltip. The tooltip matters: it is the one
    /// signal that survives the bar being hidden and the menu being closed.
    /// </summary>
    private void OnUpdateStateChanged(UpdateService updates, OverlayViewModel overlay)
    {
        var ready = updates.State == UpdateState.ReadyToRestart;

        overlay.SetUpdateReady(ready, updates.PendingVersion);
        RefreshTrayTooltip();

        if (ready)
        {
            _logger?.LogInformation("Update {Version} is staged and has been announced", updates.PendingVersion);
        }
    }

    /// <summary>
    /// The tray tooltip: what a staged update is offering, or the key to hold to dictate.
    ///
    /// It has to be built from settings rather than written in XAML, because it names a key the user
    /// can change. It was a literal "Right Ctrl" in the markup, corrected only as a side effect of an
    /// update-state change — so anybody who rebound their key was told by the tray to hold one that
    /// does nothing, until an update check happened to come along and overwrite it. Rebuilt at
    /// startup, when the settings are saved, and when the update state moves.
    /// </summary>
    private void RefreshTrayTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        var updates = Services.GetRequiredService<UpdateService>();
        var hotkey = Hotkey.ParseOrDefault(Services.GetRequiredService<SettingsStore>().Current.Hotkey);

        _tray.ToolTipText = updates.State == UpdateState.ReadyToRestart
            ? $"TawkType — {updates.PendingVersion} ready, restart to apply"
            : $"TawkType — hold {hotkey} to dictate";
    }

    /// <summary>
    /// First run, and the walkthrough anyone can ask for again from the tray. It saves as it goes and
    /// drives the real pipeline, so the only thing the app has to do here is notice when a model has
    /// arrived — the bar is sitting there saying there is none.
    /// </summary>
    private void ShowSetup()
    {
        if (_setupWindow is { IsLoaded: true })
        {
            _setupWindow.Show();
            _setupWindow.Activate();
            return;
        }

        var viewModel = Services.GetRequiredService<SetupViewModel>();
        viewModel.ModelInstalled += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = WarmUpAsync(Services.GetRequiredService<OverlayViewModel>()));

        _setupWindow = new SetupWindow(viewModel);
        _setupWindow.Closed += (_, _) =>
        {
            viewModel.Dispose(); // transient, and it holds the microphone and the engine's events
            _setupWindow = null;
        };

        _setupWindow.Show();
        _setupWindow.Activate();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = Services.GetRequiredService<SettingsViewModel>();

        // A download is the one settings action the rest of the app has to react to: the bar is sitting
        // there telling the user to do this, and the engine has nothing loaded until it is done.
        viewModel.ModelDownloaded += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = WarmUpAsync(Services.GetRequiredService<OverlayViewModel>()));

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) =>
        {
            viewModel.Dispose(); // transient, and it subscribes to the history
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.AppDataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", SettingsStore.AppDataDirectory) { UseShellExecute = true });
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Shutdown();
}
