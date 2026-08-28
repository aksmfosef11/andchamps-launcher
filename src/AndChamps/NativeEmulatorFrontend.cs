using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AndChamps;

internal static class NativeEmulatorFrontend
{
    internal const int ClientWidth = 1280;
    internal const int ClientHeight = 720;
    internal const string GameWindowTitle = "게임 창 · 포챔스에뮬레이터";

    private const int GwOwner = 4;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsSystemMenu = 0x00080000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    public static async Task<Process> WaitForWindowAsync(int port, Process emulatorLauncher,
        CancellationToken cancellationToken)
    {
        var expectedTitle = $"Android Emulator - {AvdManager.Name}:{port}";
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (emulatorLauncher.HasExited)
                throw new InvalidOperationException(
                    "Android 에뮬레이터가 게임 창을 만들기 전에 종료됐습니다. GPU 드라이버를 확인해 주세요.");

            var window = FindWindow((candidate, processId, title, className) =>
                title.Equals(expectedTitle, StringComparison.Ordinal)
                && className.Contains("QWindowIcon", StringComparison.Ordinal));
            if (window != nint.Zero)
            {
                GetWindowThreadProcessId(window, out var processId);
                var frontend = Process.GetProcessById(unchecked((int)processId));
                Hide(frontend);
                return frontend;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        throw new TimeoutException("Android 네이티브 게임 창을 30초 안에 준비하지 못했습니다.");
    }

    public static void Hide(Process frontend)
    {
        var mainWindow = FindMainWindow(frontend.Id);
        if (mainWindow == nint.Zero)
            return;
        ShowWindow(mainWindow, SwHide);
        HideOwnedToolbar(frontend.Id, mainWindow);
    }

    public static async Task ShowGameWindowAsync(Process frontend, CancellationToken cancellationToken)
    {
        if (frontend.HasExited)
            throw new InvalidOperationException("Android 게임 창이 표시되기 전에 종료됐습니다.");

        var mainWindow = FindMainWindow(frontend.Id);
        if (mainWindow == nint.Zero)
            throw new InvalidOperationException("Android 네이티브 게임 창을 찾지 못했습니다.");

        SetWindowLong(mainWindow, GwlStyle,
            BuildGameWindowStyle(GetWindowLong(mainWindow, GwlStyle)));
        SetWindowText(mainWindow, GameWindowTitle);
        ResizeForExactClientArea(mainWindow);
        ShowWindow(mainWindow, SwShow);

        // Qt가 메인 창 표시 직후 도구 창을 한 번 더 표시할 수 있어 짧게 반복합니다.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            HideOwnedToolbar(frontend.Id, mainWindow);
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        SetForegroundWindow(mainWindow);
    }

    internal static int BuildGameWindowStyle(int currentStyle) =>
        (currentStyle | WsSystemMenu | WsMinimizeBox) & ~WsMaximizeBox;

    private static void ResizeForExactClientArea(nint window)
    {
        var style = GetWindowLong(window, GwlStyle);
        var extendedStyle = GetWindowLong(window, GwlExStyle);
        var required = new NativeRect { Right = ClientWidth, Bottom = ClientHeight };
        try
        {
            var dpi = GetDpiForWindow(window);
            AdjustWindowRectExForDpi(ref required, style, false, extendedStyle, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            AdjustWindowRectEx(ref required, style, false, extendedStyle);
        }

        var width = required.Right - required.Left;
        var height = required.Bottom - required.Top;
        var workingArea = Screen.FromHandle(window).WorkingArea;
        var left = workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2);
        var top = workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2);
        SetWindowPos(window, nint.Zero, left, top, width, height, SwpNoZOrder | SwpFrameChanged);
    }

    private static nint FindMainWindow(int targetProcessId) =>
        FindWindow((candidate, processId, title, className) =>
            processId == unchecked((uint)targetProcessId)
            && GetWindow(candidate, GwOwner) == nint.Zero
            && className.Contains("QWindowIcon", StringComparison.Ordinal));

    private static void HideOwnedToolbar(int targetProcessId, nint mainWindow)
    {
        EnumWindows((candidate, _) =>
        {
            GetWindowThreadProcessId(candidate, out var processId);
            if (processId != unchecked((uint)targetProcessId)
                || GetWindow(candidate, GwOwner) != mainWindow)
                return true;

            var className = ReadClassName(candidate);
            if (className.Contains("QWindowTool", StringComparison.Ordinal))
                ShowWindow(candidate, SwHide);
            return true;
        }, nint.Zero);
    }

    private static nint FindWindow(Func<nint, uint, string, string, bool> predicate)
    {
        nint match = nint.Zero;
        EnumWindows((candidate, _) =>
        {
            GetWindowThreadProcessId(candidate, out var processId);
            if (!predicate(candidate, processId, ReadWindowText(candidate), ReadClassName(candidate)))
                return true;
            match = candidate;
            return false;
        }, nint.Zero);
        return match;
    }

    private static string ReadWindowText(nint window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static string ReadClassName(nint window)
    {
        var text = new StringBuilder(256);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectEx(ref NativeRect rectangle, int style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu, int extendedStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref NativeRect rectangle, int style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu, int extendedStyle, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int left, int top,
        int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
