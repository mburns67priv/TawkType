using System.Runtime.InteropServices;

namespace Talk2Me.Windows.Shell;

/// <summary>
/// Tells the taskbar which icon to use for a window's button.
///
/// Normally the button takes the window's own icon. But once anything sets a process-wide
/// AppUserModelID — Velopack does, so that installs, shortcuts and updates hang together — Windows
/// resolves the button's icon through that identity instead, and shows a generic one when it cannot
/// find a matching shortcut. Setting RelaunchIconResource on the window says explicitly which icon to
/// use, and works whether or not the app is installed.
/// </summary>
public static class TaskbarIdentity
{
    private const int VtLpwstr = 31;

    // PKEY_AppUserModel_RelaunchIconResource, from the AppUserModel property set.
    private static readonly PropertyKey RelaunchIconResource =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 3);

    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>
    /// Points the window's taskbar button at an icon, as "path,index" — e.g. the app's own executable.
    /// Silently does nothing if the shell refuses; a wrong icon is not worth failing a launch over.
    /// </summary>
    public static void SetTaskbarIcon(nint window, string iconPath, int iconIndex = 0)
    {
        if (window == 0 || string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        var storeId = PropertyStoreId;
        IPropertyStore? store = null;
        var value = nint.Zero;

        try
        {
            if (SHGetPropertyStoreForWindow(window, ref storeId, out store) != 0 || store is null)
            {
                return;
            }

            value = Marshal.StringToCoTaskMemUni($"{iconPath},{iconIndex}");
            var variant = new PropVariant { Type = VtLpwstr, Pointer = value };
            var key = RelaunchIconResource;

            store.SetValue(ref key, ref variant);
            store.Commit();
        }
        catch (Exception)
        {
            // Shell interop is best-effort here.
        }
        finally
        {
            if (value != nint.Zero)
            {
                Marshal.FreeCoTaskMem(value);
            }

            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

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
