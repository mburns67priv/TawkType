using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using Velopack;
using Velopack.Sources;

namespace TawkType.Desktop.Services;

public enum UpdateState
{
    Idle,
    Checking,
    Downloading,

    /// <summary>Downloaded and staged. It is applied the next time TawkType starts, or on request.</summary>
    ReadyToRestart,

    UpToDate,

    /// <summary>Running from a plain build rather than an install, so there is nothing to update.</summary>
    NotInstalled,

    Failed,
}

/// <summary>
/// Checks GitHub Releases for a newer build and stages it. Updates are downloaded quietly in the
/// background and applied on the next start, so a dictation is never interrupted by an update.
///
/// **The automatic half is off unless the user turns it on.** This is the only part of TawkType that
/// would otherwise reach the network with nobody asking: recognition and every post-processing step
/// run on the machine, and the Claude rewrite is already opt-in. A check sends nothing about the user,
/// but it is still a connection on a schedule they did not choose, so it waits to be asked.
/// <see cref="CheckAsync"/> itself is never gated — pressing *Check now* is the asking.
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// Where installed copies look for updates. Builds made before the repository was renamed have
    /// the old URL compiled in; GitHub redirects it, which is the only reason those copies can still
    /// reach a release. Do not rely on that for anything new.
    /// </summary>
    public const string RepositoryUrl = "https://github.com/JupitorStudioDev/TawkType";

    /// <summary>Long enough after launch that the model warm-up has the machine to itself.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly ILogger<UpdateService> _logger;
    private readonly ISettingsProvider _settings;
    private readonly UpdateManager? _manager;

    private UpdateInfo? _staged;

    public UpdateService(ILogger<UpdateService> logger, ISettingsProvider settings)
    {
        _logger = logger;
        _settings = settings;

        try
        {
            // The repository is public, so no token: updates work for anyone who installed the app.
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start the updater");
        }
    }

    public event EventHandler? Changed;

    public UpdateState State { get; private set; } = UpdateState.Idle;

    /// <summary>Version waiting to be applied, when there is one.</summary>
    public string? PendingVersion { get; private set; }

    public string CurrentVersion =>
        _manager?.IsInstalled == true
            ? _manager.CurrentVersion?.ToString() ?? "?"
            : typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>False when running from `dotnet run` or a plain publish, where updating means nothing.</summary>
    public bool IsInstalled => _manager?.IsInstalled == true;

    /// <summary>Whether the user has allowed TawkType to check on its own. Read live, not cached.</summary>
    public bool ChecksAutomatically => _settings.Current.CheckForUpdatesAutomatically;

    public string Describe() => State switch
    {
        UpdateState.Checking => "Checking for updates…",
        UpdateState.Downloading => "Downloading update…",
        UpdateState.ReadyToRestart => $"Version {PendingVersion} is ready — it installs when TawkType restarts.",
        UpdateState.UpToDate => $"TawkType {CurrentVersion} is up to date.",
        UpdateState.NotInstalled => $"TawkType {CurrentVersion}, running from a local build. Updates apply to installed copies.",
        UpdateState.Failed => "Could not check for updates. TawkType carries on working.",

        // The resting state says which of the two it is, because "TawkType 0.7.3" alone reads like a
        // version label and leaves somebody waiting for an update that is never going to be looked for.
        _ when !ChecksAutomatically =>
            $"TawkType {CurrentVersion}. It does not look for updates on its own — use Check now.",
        _ => $"TawkType {CurrentVersion}",
    };

    /// <summary>
    /// The background loop: a minute after launch, then every few hours for as long as the app runs —
    /// and only on the rounds where the user has allowed automatic checks. It keeps running either way
    /// so that switching them on takes effect without a restart.
    /// </summary>
    public async Task RunInBackgroundAsync(CancellationToken cancellationToken)
    {
        if (_manager?.IsInstalled != true)
        {
            SetState(UpdateState.NotInstalled);
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Read every time round rather than once at the top: the loop outlives the settings
                // window, so switching this off has to stop the next check, not the next launch.
                if (ChecksAutomatically)
                {
                    await CheckAsync(cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(CheckInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Checks and, if there is something newer, downloads it ready for the next start.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manager?.IsInstalled != true)
        {
            SetState(UpdateState.NotInstalled);
            return;
        }

        if (State is UpdateState.Checking or UpdateState.Downloading)
        {
            return;
        }

        try
        {
            SetState(UpdateState.Checking);
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (update is null)
            {
                SetState(UpdateState.UpToDate);
                return;
            }

            _logger.LogInformation("Update available: {Version}", update.TargetFullRelease.Version);
            PendingVersion = update.TargetFullRelease.Version.ToString();

            SetState(UpdateState.Downloading);
            await _manager.DownloadUpdatesAsync(update, progress: null, cancellationToken).ConfigureAwait(false);

            _staged = update;
            SetState(UpdateState.ReadyToRestart);
            _logger.LogInformation("Update {Version} staged for the next start", PendingVersion);
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, a bad release — none of it should disturb dictation.
            _logger.LogWarning(ex, "Update check failed");
            SetState(UpdateState.Failed);
        }
    }

    /// <summary>Applies a staged update and restarts. Only call when the user asks.</summary>
    public void RestartAndUpdate()
    {
        if (_manager is null || _staged is null)
        {
            return;
        }

        try
        {
            _manager.ApplyUpdatesAndRestart(_staged.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying the update failed");
            SetState(UpdateState.Failed);
        }
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
