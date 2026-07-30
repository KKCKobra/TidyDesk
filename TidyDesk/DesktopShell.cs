using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TidyDesk;

internal static class DesktopShell
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmGetItemTextW = LvmFirst + 115;
    private const uint LvmGetItemPosition = LvmFirst + 16;
    private const uint LvmSetItemPosition32 = LvmFirst + 49;
    private const uint LvmGetItemSpacing = LvmFirst + 51;
    private const uint LvifText = 0x0001;
    private const int GwlStyle = -16;
    private const int LvsAutoArrange = 0x0100;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    public static IReadOnlyList<DesktopIconInfo> GetIcons()
    {
        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Windows Explorer’s desktop icon view could not be found.");
        }

        var count = checked(
            (int)SendMessage(listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero));
        if (count == 0)
        {
            return [];
        }

        GetWindowThreadProcessId(listView, out var processId);
        using var process = Process.GetProcessById(checked((int)processId));
        if (Environment.Is64BitProcess != Is64BitProcess(process.Handle))
        {
            throw new InvalidOperationException(
                "TidyDesk and Windows Explorer must use the same architecture.");
        }

        var access = ProcessQueryInformation |
                     ProcessVmOperation |
                     ProcessVmRead |
                     ProcessVmWrite;
        var processHandle = OpenProcess(access, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows Explorer could not be opened for desktop icon discovery.");
        }

        try
        {
            return AttachDesktopPaths(ReadIcons(listView, processHandle, count));
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    public static Size GetIconSpacing()
    {
        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Windows Explorerâ€™s desktop icon view could not be found.");
        }

        return GetIconSpacing(listView);
    }

    public static ApplyResult ApplyPositions(IReadOnlyList<IconPlacement> placements)
    {
        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Windows Explorer’s desktop icon view could not be found.");
        }

        DisableAutoArrange(listView);
        var iconSpacing = GetIconSpacing(listView);
        var currentListView = FindDesktopListView();
        if (currentListView != IntPtr.Zero && currentListView != listView)
        {
            listView = currentListView;
            DisableAutoArrange(listView);
            iconSpacing = GetIconSpacing(listView);
        }

        var currentIcons = GetIcons();
        var indexes = currentIcons.ToDictionary(
            icon => icon.DisplayName,
            icon => icon.ShellIndex,
            StringComparer.CurrentCultureIgnoreCase);

        var missing = new List<string>();
        var pending = new List<(IconPlacement Placement, int Index)>();
        foreach (var placement in placements)
        {
            if (!indexes.TryGetValue(placement.DisplayName, out var index))
            {
                missing.Add(placement.DisplayName);
                continue;
            }

            pending.Add((placement, index));
        }

        GetWindowThreadProcessId(listView, out var processId);
        var access = ProcessQueryInformation |
                     ProcessVmOperation |
                     ProcessVmRead |
                     ProcessVmWrite;
        var processHandle = OpenProcess(access, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows Explorer could not be opened for icon positioning.");
        }

        var pointSize = Marshal.SizeOf<NativePoint>();
        var remotePoint = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            checked((nuint)pointSize),
            MemCommit | MemReserve,
            PageReadWrite);
        if (remotePoint == IntPtr.Zero)
        {
            CloseHandle(processHandle);
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Memory for desktop icon positioning could not be allocated.");
        }

        var positioned = 0;
        try
        {
            const int maximumAttempts = 5;
            for (var attempt = 0; attempt < maximumAttempts && pending.Count > 0; attempt++)
            {
                DisableAutoArrange(listView);
                foreach (var item in pending)
                {
                    WriteRemotePoint(
                        processHandle,
                        remotePoint,
                        item.Placement.Position);
                    SendMessage(
                        listView,
                        LvmSetItemPosition32,
                        (nint)item.Index,
                        remotePoint);
                }

                Thread.Sleep(75);

                var retry = new List<(IconPlacement Placement, int Index)>();
                foreach (var item in pending)
                {
                    if (TryReadPosition(
                            listView,
                            processHandle,
                            remotePoint,
                            item.Index,
                            out var actualPosition) &&
                        IsNearTarget(
                            actualPosition,
                            item.Placement.Position,
                            iconSpacing))
                    {
                        positioned++;
                    }
                    else
                    {
                        retry.Add(item);
                    }
                }

                pending = retry;
            }

            missing.AddRange(pending.Select(item => item.Placement.DisplayName));
        }
        finally
        {
            VirtualFreeEx(processHandle, remotePoint, 0, MemRelease);
            CloseHandle(processHandle);
        }

        if (placements.Count > 0 && positioned == 0)
        {
            throw new InvalidOperationException(
                "Windows Explorer rejected every icon position. " +
                "Make sure “Auto arrange icons” is turned off, then try again.");
        }

        return new ApplyResult(positioned, missing);
    }

    public static IReadOnlyList<IconPlacement> GetPositions(
        IReadOnlyCollection<DesktopIconInfo> requestedIcons)
    {
        if (requestedIcons.Count == 0)
        {
            return [];
        }

        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Windows Explorer’s desktop icon view could not be found.");
        }

        var indexes = GetIcons().ToDictionary(
            icon => icon.DisplayName,
            icon => icon.ShellIndex,
            StringComparer.CurrentCultureIgnoreCase);
        GetWindowThreadProcessId(listView, out var processId);
        var access = ProcessQueryInformation |
                     ProcessVmOperation |
                     ProcessVmRead |
                     ProcessVmWrite;
        var processHandle = OpenProcess(access, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows Explorer could not be opened for position discovery.");
        }

        var pointSize = Marshal.SizeOf<NativePoint>();
        var remotePoint = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            checked((nuint)pointSize),
            MemCommit | MemReserve,
            PageReadWrite);
        if (remotePoint == IntPtr.Zero)
        {
            CloseHandle(processHandle);
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Memory for desktop position discovery could not be allocated.");
        }

        try
        {
            var positions = new List<IconPlacement>(requestedIcons.Count);
            foreach (var icon in requestedIcons)
            {
                if (!indexes.TryGetValue(icon.DisplayName, out var index))
                {
                    continue;
                }

                if (!TryReadPosition(
                        listView,
                        processHandle,
                        remotePoint,
                        index,
                        out var position))
                {
                    continue;
                }

                positions.Add(
                    new IconPlacement(
                        icon.DisplayName,
                        index,
                        position));
            }

            return positions;
        }
        finally
        {
            VirtualFreeEx(processHandle, remotePoint, 0, MemRelease);
            CloseHandle(processHandle);
        }
    }

    internal static bool IsNearTarget(Point actual, Point target)
        => IsNearTarget(actual, target, LayoutEngine.DefaultIconSpacing);

    internal static bool IsNearTarget(Point actual, Point target, Size iconSpacing)
    {
        var horizontalTolerance = Math.Max(1, iconSpacing.Width / 2);
        var verticalTolerance = Math.Max(1, iconSpacing.Height / 2);
        return Math.Abs(actual.X - target.X) <= horizontalTolerance &&
               Math.Abs(actual.Y - target.Y) <= verticalTolerance;
    }

    internal static Size DecodeIconSpacing(nint packedSpacing)
    {
        var packed = unchecked((uint)packedSpacing.ToInt64());
        var width = (int)(packed & 0xFFFF);
        var height = (int)((packed >> 16) & 0xFFFF);
        return width is >= 16 and <= 1024 && height is >= 16 and <= 1024
            ? new Size(width, height)
            : LayoutEngine.DefaultIconSpacing;
    }

    private static void WriteRemotePoint(
        IntPtr processHandle,
        IntPtr remotePoint,
        Point position)
    {
        var bytes = StructureToBytes(
            new NativePoint
            {
                X = position.X,
                Y = position.Y,
            });
        if (!WriteProcessMemory(
                processHandle,
                remotePoint,
                bytes,
                bytes.Length,
                out _))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "A desktop icon position could not be prepared.");
        }
    }

    private static Size GetIconSpacing(IntPtr listView) =>
        DecodeIconSpacing(
            SendMessage(
                listView,
                LvmGetItemSpacing,
                IntPtr.Zero,
                IntPtr.Zero));

    private static bool TryReadPosition(
        IntPtr listView,
        IntPtr processHandle,
        IntPtr remotePoint,
        int index,
        out Point position)
    {
        position = Point.Empty;
        var succeeded = SendMessage(
            listView,
            LvmGetItemPosition,
            (nint)index,
            remotePoint);
        if (succeeded == IntPtr.Zero)
        {
            return false;
        }

        var bytes = new byte[Marshal.SizeOf<NativePoint>()];
        if (!ReadProcessMemory(
                processHandle,
                remotePoint,
                bytes,
                bytes.Length,
                out _))
        {
            return false;
        }

        position = new Point(
            BitConverter.ToInt32(bytes, 0),
            BitConverter.ToInt32(bytes, sizeof(int)));
        return true;
    }

    private static IReadOnlyList<DesktopIconInfo> AttachDesktopPaths(
        IReadOnlyList<DesktopIconInfo> icons)
    {
        var pathsByDisplayName = new Dictionary<string, string>(
            StringComparer.CurrentCultureIgnoreCase);
        var desktopDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        foreach (var directory in desktopDirectories.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var fileName = Path.GetFileName(path);
                    pathsByDisplayName.TryAdd(fileName, path);

                    var nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
                    if (nameWithoutExtension.Length > 0)
                    {
                        pathsByDisplayName.TryAdd(nameWithoutExtension, path);
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A protected shared-desktop entry should not prevent the remaining
                // icons from being discovered.
            }
        }

        return icons
            .Select(
                icon => new DesktopIconInfo
                {
                    DisplayName = icon.DisplayName,
                    ShellIndex = icon.ShellIndex,
                    SourcePath = pathsByDisplayName.GetValueOrDefault(icon.DisplayName),
                })
            .ToList();
    }

    private static IReadOnlyList<DesktopIconInfo> ReadIcons(
        IntPtr listView,
        IntPtr processHandle,
        int count)
    {
        const int characterCapacity = 520;
        var textBytes = characterCapacity * sizeof(char);
        var itemSize = Marshal.SizeOf<LvItem>();
        var remoteMemory = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            checked((nuint)(itemSize + textBytes)),
            MemCommit | MemReserve,
            PageReadWrite);

        if (remoteMemory == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Memory for desktop icon discovery could not be allocated.");
        }

        try
        {
            var remoteText = remoteMemory + itemSize;
            var icons = new List<DesktopIconInfo>(count);

            for (var index = 0; index < count; index++)
            {
                var item = new LvItem
                {
                    Mask = LvifText,
                    Item = index,
                    SubItem = 0,
                    Text = remoteText,
                    TextMax = characterCapacity,
                };

                var itemBytes = StructureToBytes(item);
                if (!WriteProcessMemory(
                        processHandle,
                        remoteMemory,
                        itemBytes,
                        itemBytes.Length,
                        out _))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "A desktop icon name request could not be prepared.");
                }

                SendMessage(listView, LvmGetItemTextW, (nint)index, remoteMemory);

                var buffer = new byte[textBytes];
                if (!ReadProcessMemory(
                        processHandle,
                        remoteText,
                        buffer,
                        buffer.Length,
                        out _))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "A desktop icon name could not be read.");
                }

                var displayName = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                if (displayName.Length > 0)
                {
                    icons.Add(
                        new DesktopIconInfo
                        {
                            DisplayName = displayName,
                            ShellIndex = index,
                        });
                }
            }

            return icons;
        }
        finally
        {
            VirtualFreeEx(processHandle, remoteMemory, 0, MemRelease);
        }
    }

    private static void DisableAutoArrange(IntPtr listView)
    {
        var style = GetWindowLong(listView, GwlStyle);
        if ((style & LvsAutoArrange) == 0)
        {
            return;
        }

        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLong(
            listView,
            GwlStyle,
            style & ~LvsAutoArrange);
        if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Windows Explorer’s Auto Arrange setting could not be disabled.");
        }

        if ((GetWindowLong(listView, GwlStyle) & LvsAutoArrange) != 0)
        {
            throw new InvalidOperationException(
                "Turn off “Auto arrange icons” on the desktop, then apply the layout again.");
        }
    }

    private static IntPtr FindDesktopListView()
    {
        var programManager = FindWindow("Progman", null);
        var shellView = FindWindowEx(
            programManager,
            IntPtr.Zero,
            "SHELLDLL_DefView",
            null);

        if (shellView != IntPtr.Zero)
        {
            return FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
        }

        IntPtr listView = IntPtr.Zero;
        EnumWindows(
            (window, _) =>
            {
                var view = FindWindowEx(
                    window,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);
                if (view == IntPtr.Zero)
                {
                    return true;
                }

                listView = FindWindowEx(
                    view,
                    IntPtr.Zero,
                    "SysListView32",
                    "FolderView");
                return listView == IntPtr.Zero;
            },
            IntPtr.Zero);

        return listView;
    }

    private static byte[] StructureToBytes<T>(T value)
        where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var bytes = new byte[size];
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            Marshal.Copy(pointer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static bool Is64BitProcess(IntPtr processHandle)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        if (!IsWow64Process(processHandle, out var isWow64))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return !isWow64;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LvItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public IntPtr Text;
        public int TextMax;
        public int Image;
        public IntPtr Parameter;
        public int Indent;
        public int GroupId;
        public uint ColumnCount;
        public IntPtr Columns;
        public IntPtr ColumnFormats;
        public int Group;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string className,
        string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        IntPtr window,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint allocationType,
        uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        byte[] buffer,
        int size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        byte[] buffer,
        int size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(
        IntPtr process,
        [MarshalAs(UnmanagedType.Bool)] out bool isWow64);
}
