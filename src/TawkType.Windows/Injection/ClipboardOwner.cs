using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace TawkType.Windows.Injection;

/// <summary>
/// A message-only window that owns every clipboard operation TawkType performs, and the thread that
/// pumps it.
///
/// Two things force this. Microsoft documents that opening the clipboard with a null window makes
/// <c>EmptyClipboard</c> set the owner to null, "this causes SetClipboardData to fail" — so a real
/// window handle is not optional. And a clipboard owner is sent messages by the system and by
/// clipboard viewers, so the thread holding it has to pump, which rules out the pipeline's thread pool
/// threads and the WPF UI thread alike: the first do not pump and the second must not be blocked
/// behind a clipboard another process is holding open.
///
/// Everything is marshalled onto the one thread, so clipboard work is serialised against itself
/// without a lock and the window is only ever used from the thread that created it.
/// </summary>
internal static class ClipboardOwner
{
    private const int ErrorClassAlreadyExists = 1410;
    private const uint QsAllInput = 0x04FF;
    private const uint InfiniteTimeout = 0xFFFFFFFF;
    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;
    private const int HwndMessage = -3;

    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<Action> Work = new();
    private static readonly SemaphoreSlim Pending = new(0);

    // The delegate is handed to Win32 and has to outlive every message the window is ever sent.
    private static WndProc? _wndProc;
    private static nint _handle;
    private static Thread? _thread;

    /// <summary>The window handle to open the clipboard with. Blocks until the window exists.</summary>
    public static nint Handle
    {
        get
        {
            Start();
            return _handle;
        }
    }

    /// <summary>Runs <paramref name="work"/> on the thread that owns the window, and waits for it.</summary>
    public static T Run<T>(Func<T> work)
    {
        Start();

        if (Thread.CurrentThread == _thread)
        {
            return work();
        }

        var done = new ManualResetEventSlim(false);
        var result = default(T)!;
        Exception? failure = null;

        Work.Enqueue(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        });

        Pending.Release();
        done.Wait();

        if (failure is not null)
        {
            // Rethrown on the caller's thread as itself — a Win32Exception from the clipboard has to
            // still be a Win32Exception by the time the injector decides what to do about it.
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    public static void Run(Action work) => Run<object?>(() =>
    {
        work();
        return null;
    });

    private static void Start()
    {
        if (_handle != 0)
        {
            return;
        }

        lock (Gate)
        {
            if (_handle != 0)
            {
                return;
            }

            var ready = new ManualResetEventSlim(false);

            _thread = new Thread(() => Pump(ready))
            {
                IsBackground = true,
                Name = "TawkType clipboard",
            };

            // STA because the clipboard is shell territory: anything that ever reaches OLE from here —
            // a shell extension rendering CF_HDROP, say — expects an apartment it can live in.
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();
        }
    }

    private static void Pump(ManualResetEventSlim ready)
    {
        _handle = CreateOwnerWindow();
        ready.Set();

        var waits = new[] { Pending.AvailableWaitHandle.SafeWaitHandle.DangerousGetHandle() };

        while (true)
        {
            // Wakes for a queued job or for a window message, and sleeps otherwise — a polling loop
            // here would cost wakeups all day for a window that does nothing most of the time.
            MsgWaitForMultipleObjects(1, waits, false, InfiniteTimeout, QsAllInput);

            while (PeekMessage(out var message, 0, 0, 0, PmRemove))
            {
                if (message.message == WmQuit)
                {
                    return;
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            while (Work.TryDequeue(out var job))
            {
                Pending.Wait(0);
                job();
            }
        }
    }

    private static nint CreateOwnerWindow()
    {
        _wndProc = DefWindowProc;

        var className = "TawkType.ClipboardOwner";
        var windowClass = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };

        if (RegisterClass(ref windowClass) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
            {
                throw new System.ComponentModel.Win32Exception(error, "Could not register the clipboard owner window class");
            }
        }

        // HWND_MESSAGE: no screen presence, no taskbar button, no z-order — it exists to be a handle.
        var handle = CreateWindowEx(
            0, className, className, 0, 0, 0, 0, 0, HwndMessage, 0, windowClass.hInstance, 0);

        if (handle == 0)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the clipboard owner window");
        }

        return handle;
    }

    private delegate nint WndProc(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjects(uint nCount, nint[] pHandles, bool bWaitAll, uint dwMilliseconds, uint dwWakeMask);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg lpMsg);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
