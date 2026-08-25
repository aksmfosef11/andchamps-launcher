param(
    [string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64\포챔스에뮬레이터.exe'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\screenshots')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
$windowHelperSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class AndChampsWindow
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    private static string TextOf(IntPtr window)
    {
        var text = new StringBuilder(GetWindowTextLength(window) + 1);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    public static string[] Texts(IntPtr parent)
    {
        var texts = new List<string>();
        var title = TextOf(parent);
        if (title.Length > 0)
            texts.Add(title);
        EnumChildWindows(parent, (window, _) =>
        {
            var text = TextOf(window);
            if (text.Length > 0)
                texts.Add(text);
            return true;
        }, IntPtr.Zero);
        return texts.ToArray();
    }

    public static IntPtr FindChild(IntPtr parent, string expectedText)
    {
        var found = IntPtr.Zero;
        EnumChildWindows(parent, (window, _) =>
        {
            if (TextOf(window) != expectedText)
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindTop(uint processId, string expectedTitle)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var candidateProcessId);
            if (candidateProcessId != processId || TextOf(window) != expectedTitle)
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static void Click(IntPtr window) => PostMessage(window, 0x00F5, IntPtr.Zero, IntPtr.Zero);

    public static int[] Bounds(IntPtr window)
    {
        if (GetWindowRect(window, out var rect) == false)
            throw new InvalidOperationException("GetWindowRect failed.");
        return new[] { rect.Left, rect.Top, rect.Right, rect.Bottom };
    }

    public static void Activate(IntPtr window) => SetForegroundWindow(window);
    public static bool Print(IntPtr window, IntPtr deviceContext) => PrintWindow(window, deviceContext, 2);
}
'@
Add-Type -TypeDefinition $windowHelperSource

function Save-WindowScreenshot([IntPtr]$Window, [string]$Path) {
    $bounds = [AndChampsWindow]::Bounds($Window)
    [AndChampsWindow]::Activate($Window)
    Start-Sleep -Milliseconds 350
    $bitmap = [Drawing.Bitmap]::new($bounds[2] - $bounds[0], $bounds[3] - $bounds[1])
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $deviceContext = $graphics.GetHdc()
            try {
                if ([AndChampsWindow]::Print($Window, $deviceContext) -eq $false)
                    { throw 'PrintWindow failed.' }
            }
            finally {
                $graphics.ReleaseHdc($deviceContext)
            }
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$runtimeRoot = Join-Path ([IO.Path]::GetTempPath()) ('AndChamps.Screenshot.' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
$env:ANDCHAMPS_RUNTIME_ROOT = $runtimeRoot
$process = Start-Process -FilePath $resolvedExecutable -PassThru

try {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        $mainWindow = $process.MainWindowHandle
    } while ($mainWindow -eq 0 -and (Get-Date) -lt $deadline)
    if ($mainWindow -eq 0)
        { throw 'Launcher window did not appear.' }

    $homePath = Join-Path $OutputDirectory 'launcher-home.png'
    Save-WindowScreenshot $mainWindow $homePath
    $homeTexts = [AndChampsWindow]::Texts($mainWindow)

    $installButton = [AndChampsWindow]::FindChild($mainWindow, '설치')
    if ($installButton -eq 0)
        { throw 'Install button was not found.' }
    [AndChampsWindow]::Click($installButton)

    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        $licenseDialog = [AndChampsWindow]::FindTop([uint32]$process.Id, '포챔스에뮬레이터 첫 설치')
    } while ($licenseDialog -eq 0 -and (Get-Date) -lt $deadline)
    if ($licenseDialog -eq 0)
        { throw 'License dialog did not appear.' }

    $licensePath = Join-Path $OutputDirectory 'android-sdk-license.png'
    Save-WindowScreenshot $licenseDialog $licensePath
    $licenseTexts = [AndChampsWindow]::Texts($licenseDialog)
    $cancelButton = [AndChampsWindow]::FindChild($licenseDialog, '취소')
    if ($cancelButton -ne 0)
        { [AndChampsWindow]::Click($cancelButton) }

    $forbidden = @($homeTexts + $licenseTexts) |
        Where-Object { $_ -match '(?i)포켓몬|pokemon|pokémon|챔피언|champions' }
    if ($forbidden.Count -gt 0)
        { throw "Trademark text remains in displayed controls: $($forbidden -join ', ')" }

    [pscustomobject]@{
        Executable = $resolvedExecutable
        ProductVersion = (Get-Item -LiteralPath $resolvedExecutable).VersionInfo.ProductVersion
        HomeScreenshot = $homePath
        LicenseScreenshot = $licensePath
        DisplayedTextCheck = 'passed'
    }
}
finally {
    Start-Sleep -Milliseconds 500
    if ($process.HasExited -eq $false) {
        $null = $process.CloseMainWindow()
        Start-Sleep -Milliseconds 500
    }
    if ($process.HasExited -eq $false)
        { Stop-Process -Id $process.Id -Force }
    Remove-Item -LiteralPath $runtimeRoot -Recurse -Force -ErrorAction SilentlyContinue
}
