namespace TawkType.Core.Settings;

/// <summary>Whether a folder may be deleted, and why not when it may not.</summary>
public readonly record struct RemovalCheck(bool CanDelete, string? Refusal)
{
    public static RemovalCheck Allowed { get; } = new(true, null);

    public static RemovalCheck Refused(string because) => new(false, because);
}

/// <summary>
/// The guard in front of "delete everything TawkType has kept".
///
/// This exists because of gotcha 19. The data folder and the install folder differ by three letters —
/// <c>%LOCALAPPDATA%\TawkType</c> against <c>%LOCALAPPDATA%\TawkTypeApp</c> — and one of the two
/// callers here runs *during an uninstall*, with no user watching and Velopack clearing the other
/// folder at the same moment. A path built from the wrong string would take gigabytes of models, the
/// history and the encrypted key with it, and there would be nobody to notice.
///
/// So nothing recursive happens until this says so. Pure, and tested against the cases that would
/// actually hurt rather than against a notion of tidiness.
/// </summary>
public static class DataRemoval
{
    /// <summary>
    /// Below this many segments under the root, a path is something like <c>C:\Users</c> and no
    /// application has any business deleting it recursively.
    /// </summary>
    private const int MinimumDepth = 3;

    /// <summary>
    /// Whether <paramref name="dataDirectory"/> may be deleted recursively, given where the
    /// application itself is installed.
    /// </summary>
    /// <param name="dataDirectory">The folder holding settings, models, history and the key.</param>
    /// <param name="installDirectory">
    /// Where the application lives. Velopack owns it and clears it on install and uninstall, so it is
    /// never ours to delete, and a data folder that contains it is a data folder we have confused
    /// with something else.
    /// </param>
    public static RemovalCheck Check(string? dataDirectory, string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return RemovalCheck.Refused("There is no data folder to delete.");
        }

        // Before normalising, not after: GetFullPath resolves a relative path against the current
        // directory, which would quietly turn "TawkType\models" into a real folder somewhere else
        // and let it through. Caught by a test that expected the opposite.
        if (!Path.IsPathRooted(dataDirectory.Trim()))
        {
            return RemovalCheck.Refused("Only a full path can be deleted, not a relative one.");
        }

        string target;
        try
        {
            target = Normalise(dataDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RemovalCheck.Refused("That is not a usable path.");
        }

        // A drive root, or one step inside it. Whatever went wrong to produce this, deleting is not
        // the way to find out.
        if (Depth(target) < MinimumDepth)
        {
            return RemovalCheck.Refused("That path is too close to the root of the drive to delete.");
        }

        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return RemovalCheck.Allowed;
        }

        var install = Normalise(installDirectory);

        if (Same(target, install))
        {
            return RemovalCheck.Refused("That is the folder TawkType is installed in, not its data.");
        }

        if (Contains(target, install))
        {
            return RemovalCheck.Refused("That folder contains the installed application.");
        }

        return RemovalCheck.Allowed;
    }

    /// <summary>
    /// True when <paramref name="outer"/> contains <paramref name="inner"/>. Compared with a trailing
    /// separator on both, so <c>…\TawkType</c> does not appear to contain <c>…\TawkTypeApp</c> — the
    /// prefix match that makes gotcha 19 a live problem rather than a theoretical one.
    /// </summary>
    private static bool Contains(string outer, string inner)
        => inner.StartsWith(WithSeparator(outer), StringComparison.OrdinalIgnoreCase);

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string WithSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>Full path, no trailing separator, so two spellings of one folder compare equal.</summary>
    private static string Normalise(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    /// <summary>How many named segments sit below the root. <c>C:\a\b</c> is 2.</summary>
    private static int Depth(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }
}
