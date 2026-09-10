using System.Runtime.InteropServices;

namespace Talk2Me.Windows.Shell;

/// <summary>
/// Tells the taskbar which icon to use for a window's button.
///
/// Normally the button takes the window's own icon. But once anything sets a process-wide
/// AppUserModelID — Velopack does, so that installs, shortcuts and updates hang together — Windows
/// resolves the button's icon through that identity instead, and shows a generic one.
///
/// The cure has two halves, and the order matters. RelaunchIconResource is only honoured for a window
/// carrying an *explicit* AppUserModelID of its own; merely inheriting the process one is not enough.
/// And writing that ID is what makes the taskbar re-read the window's identity — so the icon must
/// already be in the property store when the ID lands, or the refresh happens without it. Setting the
/// ID first, then the icon, silently does nothing.
/// </summary>
public static class TaskbarIdentity
{
    private const int VtLpwstr = 31;

    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary>PKEY_AppUserModel_RelaunchIconResource.</summary>
    private static readonly PropertyKey RelaunchIconResource = new(AppUserModel, 3);

    /// <summary>PKEY_AppUserModel_ID.</summary>
    private static readonly PropertyKey AppUserModelId = new(AppUserModel, 5);

    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>
    /// Points the window's taskbar button at an icon, given as a path and an index into it — normally
    /// the application's own executable. Returns a short description of what happened, for the log;
    /// a wrong icon is never worth failing a launch over, so nothing here throws.
    /// </summary>
    public static string SetTaskbarIcon(nint window, string iconPath, int iconIndex = 0)
    {
        if (window == 0 || string.IsNullOrWhiteSpace(iconPath))
        {
            return "skipped: no window or icon path";
        }

        var storeId = PropertyStoreId;
        IPropertyStore? store = null;

        try
        {
            var hr = SHGetPropertyStoreForWindow(window, ref storeId, out store);
            if (hr != 0 || store is null)
            {
                return $"no property store: 0x{hr:X8}";
            }

            Set(store, RelaunchIconResource, $"{iconPath},{iconIndex}");

            // Match the process. On an installed copy that is also the AUMID on Velopack's Start Menu
            // shortcut, so the button still groups with it.
            var id = ProcessAppUserModelId();
            if (id is not null)
            {
                Set(store, AppUserModelId, id);
            }

            store.Commit();
            return $"icon {iconPath},{iconIndex}; id {id ?? "(none)"}";
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

    /// <summary>The AppUserModelID set on this process, or null when there is not one.</summary>
    private static string? ProcessAppUserModelId()
    {
        try
        {
            return GetCurrentProcessExplicitAppUserModelID(out var id) == 0 && !string.IsNullOrWhiteSpace(id)
                ? id
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int GetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] out string id);

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
