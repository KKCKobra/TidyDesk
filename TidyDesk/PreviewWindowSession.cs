using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TidyDesk;

internal sealed class PreviewWindowSession : IDisposable
{
    private const int GwlExStyle = -20;
    private const int GwOwner = 4;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const int DwmwaCloaked = 14;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    private readonly List<IntPtr> _windows;
    private bool _disposed;

    private PreviewWindowSession(List<IntPtr> windows)
    {
        _windows = windows;
    }

    public static PreviewWindowSession MinimizeOtherApplications()
    {
        var currentProcessId = (uint)Environment.ProcessId;
        var windows = new List<IntPtr>();

        EnumWindows(
            (window, _) =>
            {
                if (!IsApplicationWindow(window, currentProcessId))
                {
                    return true;
                }

                ShowWindowAsync(window, SwMinimize);
                windows.Add(window);
                return true;
            },
            IntPtr.Zero);

        WaitForMinimizeAnimations(windows);
        return new PreviewWindowSession(windows);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var window in _windows)
        {
            if (IsWindow(window) && IsIconic(window))
            {
                ShowWindowAsync(window, SwRestore);
            }
        }
    }

    private static bool IsApplicationWindow(IntPtr window, uint currentProcessId)
    {
        if (!IsWindowVisible(window) || IsIconic(window))
        {
            return false;
        }

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == currentProcessId)
        {
            return false;
        }

        var className = GetClassName(window);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        if (GetWindowTextLength(window) == 0 || IsCloaked(window))
        {
            return false;
        }

        var extendedStyle = GetWindowLong(window, GwlExStyle);
        var hasOwner = GetWindow(window, GwOwner) != IntPtr.Zero;
        var isToolWindow = (extendedStyle & WsExToolWindow) != 0;
        var isApplicationWindow = (extendedStyle & WsExAppWindow) != 0;
        return !isToolWindow && (!hasOwner || isApplicationWindow);
    }

    private static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) == 0
            ? string.Empty
            : buffer.ToString();
    }

    private static bool IsCloaked(IntPtr window)
    {
        var cloaked = 0;
        var result = DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            out cloaked,
            Marshal.SizeOf<int>());
        return result == 0 && cloaked != 0;
    }

    private static void WaitForMinimizeAnimations(IReadOnlyCollection<IntPtr> windows)
    {
        if (windows.Count == 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 750 &&
               windows.Any(window => IsWindow(window) && !IsIconic(window)))
        {
            Thread.Sleep(25);
        }

        // Give Desktop Window Manager one frame to finish the minimize animation
        // before the preview captures the desktop.
        Thread.Sleep(40);
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(
        IntPtr window,
        StringBuilder className,
        int maximumCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out int attributeValue,
        int attributeSize);
}
