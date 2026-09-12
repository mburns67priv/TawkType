using System.ComponentModel;
using System.Runtime.InteropServices;
using TawkType.Core.Text;

namespace TawkType.Windows.Injection;

/// <summary>
/// Raw Win32 clipboard access, usable from any thread: every call is marshalled onto
/// <see cref="ClipboardOwner"/>, which supplies the window handle the clipboard is opened with and
/// pumps the messages an owner is sent.
///
/// This deals in whole clipboards, not only text, because TawkType borrows the user's clipboard to
/// paste a dictation and has to give it back with everything on it.
/// </summary>
internal static class NativeClipboard
{
    public const uint CfUnicodeText = 13;

    private const uint GmemMoveable = 0x0002;

    /// <summary>Per format, and in total. A clipboard larger than this is not copied aside; see <see cref="ClipboardSnapshot"/>.</summary>
    public const int MaxFormatBytes = 16 * 1024 * 1024;
    public const int MaxTotalBytes = 48 * 1024 * 1024;

    // Formats whose handle is not memory from GlobalAlloc, so there is nothing to copy aside. Windows
    // synthesises most of them back from one that is: restoring CF_DIB brings CF_BITMAP and CF_PALETTE
    // with it, which is why an image survives this list rather than being lost to it.
    private static readonly HashSet<uint> NotMemoryBacked = new()
    {
        2,     // CF_BITMAP        — synthesised from CF_DIB / CF_DIBV5
        3,     // CF_METAFILEPICT  — synthesised from CF_ENHMETAFILE
        9,     // CF_PALETTE       — synthesised from CF_DIB / CF_DIBV5
        14,    // CF_ENHMETAFILE
        0x80,  // CF_OWNERDISPLAY
        0x82,  // CF_DSPBITMAP
        0x83,  // CF_DSPMETAFILEPICT
        0x8E,  // CF_DSPENHMETAFILE
    };

    private static uint _excludeFromMonitors;
    private static uint _canIncludeInHistory;
    private static uint _canUploadToCloud;

    /// <summary>
    /// Changes whenever anything changes the clipboard, in any process. The only way to tell "what I
    /// put there is still there" from "somebody has copied something since".
    /// </summary>
    public static uint SequenceNumber() => GetClipboardSequenceNumber();

    public static string? TryGetText() => ClipboardOwner.Run(() =>
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText) || !TryOpen())
        {
            return null;
        }

        try
        {
            var bytes = Read(CfUnicodeText);
            return bytes is null ? null : DecodeUnicodeText(bytes);
        }
        finally
        {
            CloseClipboard();
        }
    });

    /// <summary>
    /// Replaces the clipboard with a single run of text.
    /// </summary>
    /// <param name="allowClipboardHistory">
    /// Whether this text may be kept by Windows' clipboard history. False for text TawkType puts there
    /// on its own account — a dictation pasted through the clipboard is a mechanism, not something the
    /// user copied, and it should not turn up in Win+V afterwards.
    /// </param>
    /// <remarks>
    /// Nothing TawkType writes is ever allowed onto the cloud clipboard. It is the one path by which
    /// dictated words could leave the machine without anybody choosing it, and "the only thing that
    /// leaves is the optional rewrite" has to be true of the delivery path as well as the pipeline.
    /// </remarks>
    public static ClipboardWrite TrySetText(string text, bool allowClipboardHistory) => ClipboardOwner.Run(() =>
    {
        if (!TryOpen())
        {
            return ClipboardWrite.NotOpened;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return ClipboardWrite.NotOpened;
            }

            // Past this point the user's clipboard is already gone, so every failure below is Emptied:
            // the caller has to put back what it captured, whatever happened to our own write.
            var bytes = new byte[(text.Length + 1) * 2];
            System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);

            if (!Write(CfUnicodeText, bytes))
            {
                return ClipboardWrite.Emptied;
            }

            WriteMonitorPreferences(allowClipboardHistory);
            return ClipboardWrite.Written;
        }
        finally
        {
            CloseClipboard();
        }
    });

    /// <summary>Every format currently on the clipboard, copyable or not.</summary>
    public static IReadOnlyList<uint> ListFormats() => ClipboardOwner.Run(() =>
    {
        if (!TryOpen())
        {
            return (IReadOnlyList<uint>)Array.Empty<uint>();
        }

        try
        {
            var formats = new List<uint>();
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                formats.Add(format);
            }

            return formats;
        }
        finally
        {
            CloseClipboard();
        }
    });

    /// <summary>Copies every format that can be copied. Never throws: a clipboard that cannot be read is reported empty.</summary>
    public static ClipboardSnapshot Capture() => ClipboardOwner.Run(() =>
    {
        if (!TryOpen())
        {
            return ClipboardSnapshot.Unreadable;
        }

        try
        {
            var entries = new List<ClipboardSnapshot.Entry>();
            var skipped = new List<uint>();
            var total = 0;

            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                if (NotMemoryBacked.Contains(format) || IsPrivate(format))
                {
                    // Not ours to copy. Private formats are not freed by the system either, so putting
                    // one back would hand the owning application a handle it no longer expects.
                    skipped.Add(format);
                    continue;
                }

                var bytes = Read(format);
                if (bytes is null || bytes.Length > MaxFormatBytes || total + bytes.Length > MaxTotalBytes)
                {
                    skipped.Add(format);
                    continue;
                }

                total += bytes.Length;
                entries.Add(new ClipboardSnapshot.Entry(format, bytes));
            }

            var captured = entries.Select(entry => entry.Format).ToHashSet();
            var lost = skipped.Where(skip => !CanBeSynthesisedFrom(skip, captured)).ToList();

            var withheld = DropOrphanedDescriptors(entries, lost);
            return new ClipboardSnapshot(entries, lost, withheld);
        }
        catch
        {
            return ClipboardSnapshot.Unreadable;
        }
        finally
        {
            CloseClipboard();
        }
    });

    /// <summary>Puts a captured clipboard back. Returns false if any part of it could not be written.</summary>
    public static bool Restore(ClipboardSnapshot snapshot) => ClipboardOwner.Run(() =>
    {
        if (!TryOpen())
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var restored = true;
            foreach (var entry in snapshot.Entries)
            {
                restored &= Write(entry.Format, entry.Bytes);
            }

            return restored;
        }
        finally
        {
            CloseClipboard();
        }
    });

    /// <summary>Leaves the clipboard empty, which is how an empty clipboard was found.</summary>
    public static bool Clear() => ClipboardOwner.Run(() =>
    {
        if (!TryOpen())
        {
            return false;
        }

        try
        {
            return EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    });

    /// <summary>Kept for the callers that only ever deal in text; throws so a failure is not silent.</summary>
    public static void SetText(string text, bool allowClipboardHistory = true)
    {
        if (TrySetText(text, allowClipboardHistory) != ClipboardWrite.Written)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not put the text on the clipboard");
        }
    }

    /// <summary>
    /// Takes a file descriptor back out of the snapshot when its contents could not be copied.
    ///
    /// `FileContents` is how a file that is not a file travels — a mail attachment, something inside a
    /// zip — and Microsoft describes it as normally a stream (`TYMED_ISTREAM`), not memory, so there is
    /// nothing here to copy aside. Putting the descriptor back without it would leave the clipboard
    /// advertising files whose contents nobody can read: a target that prefers the descriptor, which
    /// is what a mail client does, would produce an empty attachment instead of falling back to the
    /// `CF_HDROP` path that did survive. An offer that cannot be honoured is worse than no offer.
    /// </summary>
    private static IReadOnlyList<uint> DropOrphanedDescriptors(List<ClipboardSnapshot.Entry> entries, List<uint> lost)
    {
        var withheld = new List<uint>();

        var contents = RegisterClipboardFormat("FileContents");
        if (contents == 0 || !lost.Contains(contents))
        {
            return withheld;
        }

        foreach (var name in new[] { "FileGroupDescriptorW", "FileGroupDescriptor" })
        {
            var descriptor = RegisterClipboardFormat(name);
            if (descriptor != 0 && entries.RemoveAll(entry => entry.Format == descriptor) > 0)
            {
                withheld.Add(descriptor);
            }
        }

        return withheld;
    }

    private static bool IsPrivate(uint format) => format is >= 0x0200 and <= 0x02FF;

    /// <summary>
    /// Whether Windows will put a format we did not copy back on the clipboard by itself, given what we
    /// did copy. Skipping a format is only a loss if the answer is no — measured on a real screenshot:
    /// CF_BITMAP was skipped, CF_DIB was kept, and the clipboard came back with CF_BITMAP on it.
    /// The table is the documented one at
    /// https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats.
    /// </summary>
    private static bool CanBeSynthesisedFrom(uint format, IReadOnlySet<uint> captured) => format switch
    {
        2 or 9 => captured.Contains(8) || captured.Contains(17),   // CF_BITMAP, CF_PALETTE ← CF_DIB / CF_DIBV5

        // CF_METAFILEPICT comes from CF_ENHMETAFILE and vice versa, and neither of them is memory we
        // can copy, so a metafile on the clipboard really is lost. It is the one format that is.
        _ => false,
    };

    private static string? DecodeUnicodeText(byte[] bytes)
    {
        // GlobalSize reports the allocation, which can be rounded up past the string it holds and is
        // not guaranteed to land on a whole UTF-16 code unit.
        var text = System.Text.Encoding.Unicode.GetString(bytes, 0, bytes.Length & ~1);
        var terminator = text.IndexOf('\0');
        return terminator < 0 ? text : text[..terminator];
    }

    /// <summary>Copies one format's bytes out. The clipboard must already be open.</summary>
    private static byte[]? Read(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == 0)
        {
            return null;
        }

        var size = (int)Math.Min(GlobalSize(handle), int.MaxValue);
        if (size <= 0)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Writes one format. The clipboard must already be open and emptied. On success the system owns
    /// the memory; on failure we still do, and it has to be freed here or it is leaked for the life of
    /// the process.
    /// </summary>
    private static bool Write(uint format, byte[] bytes)
    {
        var handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (handle == 0)
        {
            return false;
        }

        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            GlobalFree(handle);
            return false;
        }

        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(format, handle) == 0)
        {
            GlobalFree(handle);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The three registered formats Windows reads to decide whether clipboard content may be kept in
    /// the history or synchronised to the user's other devices. Best effort: a failure here means the
    /// paste still works and the user's own Windows settings apply, which is the status quo.
    /// </summary>
    private static void WriteMonitorPreferences(bool allowClipboardHistory)
    {
        _excludeFromMonitors = _excludeFromMonitors != 0 ? _excludeFromMonitors : RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        _canIncludeInHistory = _canIncludeInHistory != 0 ? _canIncludeInHistory : RegisterClipboardFormat("CanIncludeInClipboardHistory");
        _canUploadToCloud = _canUploadToCloud != 0 ? _canUploadToCloud : RegisterClipboardFormat("CanUploadToCloudClipboard");

        var no = BitConverter.GetBytes(0u);
        var yes = BitConverter.GetBytes(1u);

        if (!allowClipboardHistory && _excludeFromMonitors != 0)
        {
            // Documented as "place any data on the clipboard in this format"; the DWORD is arbitrary.
            Write(_excludeFromMonitors, no);
        }

        if (_canIncludeInHistory != 0)
        {
            Write(_canIncludeInHistory, allowClipboardHistory ? yes : no);
        }

        if (_canUploadToCloud != 0)
        {
            Write(_canUploadToCloud, no);
        }
    }

    private static bool TryOpen()
    {
        // Another process may hold the clipboard for a few milliseconds; retry briefly.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(ClipboardOwner.Handle))
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);
}
