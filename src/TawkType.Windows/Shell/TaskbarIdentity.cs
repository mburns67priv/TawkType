using System.Runtime.InteropServices;

namespace TawkType.Windows.Shell;

/// <summary>
/// Tells the taskbar which icon to use for a window's button.
///
/// Normally the button takes the window's own icon. But once anything sets a process-wide
/// AppUserModelID — Velopack does, so that installs, shortcuts and updates hang together — Windows
/// resolves the button's icon through that identity instead, and shows a generic one.
///
/// Two things make it come back, both measured rather than guessed:
///
/// The window needs an AppUserModelID that no installed shortcut claims. Velopack's own
/// (velopack.TawkType) is registered against its Start Menu shortcut, and for a registered ID the
/// shell serves that app's cached icon and ignores everything set here — which is exactly the generic
/// icon we were trying to replace. Under an ID nobody has registered, the property below wins.
///
/// And the icon has to be somewhere the shell will actually read it. It refuses files under
/// %LOCALAPPDATA% and %APPDATA% — which is where a per-user install and its own data both live — while
/// reading the identical bytes happily from the temp folder or anywhere outside those trees. So the
/// caller stages a copy and passes its path; see TaskbarWindow.
/// </summary>
public static class TaskbarIdentity
{
    private const int VtLpwstr = 31;

    /// <summary>
    /// Deliberately not Velopack's; see the note above. It only has to be an identity no installed
    /// shortcut claims, so it renamed with the app — "JupitorStudio.TawkType" is no more registered
    /// than the old one was.
    /// </summary>
    private const string WindowAppUserModelId = "JupitorStudio.TawkType";

    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary>PKEY_AppUserModel_RelaunchIconResource.</summary>
    private static readonly PropertyKey RelaunchIconResource = new(AppUserModel, 3);

    /// <summary>PKEY_AppUserModel_ID.</summary>
    private static readonly PropertyKey AppUserModelId = new(AppUserModel, 5);

    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>
    /// Points the window's taskbar button at an icon file. Returns a short description of what
    /// happened, for the log; a wrong icon is never worth failing a launch over, so nothing here throws.
    /// </summary>
    public static string SetTaskbarIcon(nint window, string iconFile)
    {
        if (window == 0 || string.IsNullOrWhiteSpace(iconFile))
        {
            return "skipped: no window or icon";
        }

        var storeId = PropertyStoreId;
        IPropertyStore? store = null;

        // Overridable so the two halves of this — which icon, which identity — can be varied against a
        // running taskbar without a rebuild. It took a lot of trials to find the pair that works.
        var icon = Environment.GetEnvironmentVariable("TAWKTYPE_TEST_ICONRES") is { Length: > 0 } probe
            ? probe
            : $"{iconFile},0";
        var identity = Environment.GetEnvironmentVariable("TAWKTYPE_TEST_WINDOWID") is { Length: > 0 } probeId
            ? probeId
            : WindowAppUserModelId;

        try
        {
            var hr = SHGetPropertyStoreForWindow(window, ref storeId, out store);
            if (hr != 0 || store is null)
            {
                return $"no property store: 0x{hr:X8}";
            }

            Set(store, RelaunchIconResource, icon);
            Set(store, AppUserModelId, identity);
            store.Commit();
            return $"icon {icon}; id {identity}";
        }
        catch (Exception ex)
        {
            return "failed: " + ex.Message;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    private static void Set(IPropertyStore store, PropertyKey key, string value)
    {
        var memory = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var variant = new PropVariant { Type = VtLpwstr, Pointer = memory };
            store.SetValue(ref key, ref variant);
        }
        finally
        {
            Marshal.FreeCoTaskMem(memory);
        }
    }

    /// <summary>
    /// Gives the process an AppUserModelID. Only useful for reproducing an installed copy's identity
    /// from a plain build — installs get theirs from Velopack — so it is deliberately not called by the
    /// app itself.
    /// </summary>
    public static void SetProcessAppUserModelId(string id) => SetCurrentProcessExplicitAppUserModelID(id);

    [DllImport("shell32.dll")]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string id);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        nint window,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    /// <summary>Only the VT_LPWSTR shape is needed, so the union is laid out for that one case.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Pointer;
        public nint Padding;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }
}
