using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AndChamps;

internal sealed class VirtualizationUnavailableException(string message) : InvalidOperationException(message);

internal sealed class LaunchCoordinator(AppPaths paths)
{
    public async Task<GameSession> RunAsync(IProgress<ProgressUpdate> progress,
        Func<CancellationToken, Task<SelectedGamePackage?>> selectPackage,
        CancellationToken cancellationToken)
    {
        SelectedGamePackage? selectedPackage = null;
        var configuredPackage = Environment.GetEnvironmentVariable("ANDCHAMPS_APK");
        if (!string.IsNullOrWhiteSpace(configuredPackage) && File.Exists(configuredPackage))
            selectedPackage = new SelectedGamePackage(Path.GetFullPath(configuredPackage));

        var runtime = new RuntimeProvisioner(paths);
        if (!runtime.IsReady)
            LicenseConsent.EnsureAccepted(paths);
        await runtime.EnsureAsync(progress, cancellationToken);
        await EnsureAccelerationAsync(cancellationToken);
        var avd = new AvdManager(paths);
        avd.EnsureCreated();

        var port = FindFreeEmulatorPort();
        var serial = $"emulator-{port}";
        progress.Report(new ProgressUpdate("경량 Android를 시작하고 있습니다…"));
        await ProcessRunner.CaptureAsync(paths.AdbExe, ["start-server"], avd.EnvironmentVariables,
            cancellationToken, throwOnError: false);
        var emulator = ProcessRunner.Start(paths.EmulatorExe,
        [
            $"@{AvdManager.Name}",
            "-port",
            port.ToString(),
            "-gpu",
            "host",
            "-no-window",
            "-no-boot-anim",
            "-no-metrics",
            "-camera-back",
            "none",
            "-camera-front",
            "none",
            "-netdelay",
            "none",
            "-netspeed",
            "full"
        ], avd.EnvironmentVariables, createWindow: false);
        Process? frontend = null;

        try
        {
            await WaitForBootAsync(serial, emulator, avd.EnvironmentVariables, progress, cancellationToken);
            await ApplyGameSettingsAsync(serial, avd.EnvironmentVariables, cancellationToken);
            var installed = await GameInstaller.IsInstalledAsync(paths.AdbExe, serial, avd.EnvironmentVariables, cancellationToken);
            if (!installed)
            {
                progress.Report(new ProgressUpdate("Android 준비 완료 · 게임 APK를 선택해 주세요."));
                selectedPackage ??= await selectPackage(cancellationToken);
                if (selectedPackage is null)
                    throw new OperationCanceledException("게임 APK 선택이 취소되었습니다.", cancellationToken);

                var apk = ApkInspector.Inspect(selectedPackage.Path);
                if (apk.Compatibility == ApkCompatibility.ArmOnly)
                    progress.Report(new ProgressUpdate("ARM64 게임 패키지 감지: 공식 네이티브 변환층을 사용합니다."));
                try
                {
                    await GameInstaller.InstallAsync(paths.AdbExe, serial, selectedPackage.Path,
                        avd.EnvironmentVariables, progress, cancellationToken);
                }
                finally
                {
                    if (selectedPackage.DeleteAfterInstall)
                        try { File.Delete(selectedPackage.Path); } catch { }
                }
            }

            progress.Report(new ProgressUpdate("게임을 실행합니다."));
            await GameInstaller.LaunchAsync(paths.AdbExe, serial, avd.EnvironmentVariables, cancellationToken);

            progress.Report(new ProgressUpdate("전용 게임 화면을 여는 중입니다…"));
            await WaitForGameSurfaceAsync(serial, avd.EnvironmentVariables, cancellationToken);
            var frontendEnvironment = new Dictionary<string, string>(avd.EnvironmentVariables)
            {
                ["ADB"] = paths.AdbExe
            };
            frontend = ProcessRunner.Start(paths.ScrcpyExe,
            [
                "--serial",
                serial,
                "--window-title=게임 창 · 포챔스에뮬레이터",
                "--window-width=1280",
                "--window-height=720",
                "--max-fps=60",
                "--video-bit-rate=16M",
                "--no-clipboard-autosync",
                "--disable-screensaver",
                "--no-terminal-title"
            ], frontendEnvironment, createWindow: false);

            await WaitForFrontendWindowAsync(frontend, cancellationToken);
            return new GameSession(emulator, frontend, paths.AdbExe, serial, avd.EnvironmentVariables);
        }
        catch
        {
            if (frontend is not null)
            {
                try { if (!frontend.HasExited) frontend.Kill(entireProcessTree: true); } catch { }
                frontend.Dispose();
            }
            if (!emulator.HasExited)
                emulator.Kill(entireProcessTree: true);
            emulator.Dispose();
            throw;
        }
    }

    public async Task<bool> ClearGameDataAsync(IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        var runtime = new RuntimeProvisioner(paths);
        if (!runtime.IsReady || !Directory.Exists(paths.AvdDirectory))
            return false;

        await EnsureAccelerationAsync(cancellationToken);
        var avd = new AvdManager(paths);
        avd.EnsureCreated();
        var port = FindFreeEmulatorPort();
        var serial = $"emulator-{port}";
        progress.Report(new ProgressUpdate("데이터 제거를 위해 Android를 시작하고 있습니다…"));
        await ProcessRunner.CaptureAsync(paths.AdbExe, ["start-server"], avd.EnvironmentVariables,
            cancellationToken, throwOnError: false);
        var emulator = ProcessRunner.Start(paths.EmulatorExe,
        [
            $"@{AvdManager.Name}",
            "-port",
            port.ToString(),
            "-gpu",
            "host",
            "-no-window",
            "-no-boot-anim",
            "-no-metrics",
            "-camera-back",
            "none",
            "-camera-front",
            "none"
        ], avd.EnvironmentVariables, createWindow: false);

        try
        {
            await WaitForBootAsync(serial, emulator, avd.EnvironmentVariables, progress, cancellationToken);
            if (!await GameInstaller.IsInstalledAsync(paths.AdbExe, serial, avd.EnvironmentVariables,
                    cancellationToken))
                return false;
            progress.Report(new ProgressUpdate("게임 데이터를 초기화하고 있습니다…"));
            await GameInstaller.ClearDataAsync(paths.AdbExe, serial, avd.EnvironmentVariables, cancellationToken);
            return true;
        }
        finally
        {
            await ProcessRunner.CaptureAsync(paths.AdbExe, ["-s", serial, "emu", "kill"],
                avd.EnvironmentVariables, CancellationToken.None, throwOnError: false);
            if (!emulator.HasExited)
                emulator.Kill(entireProcessTree: true);
            emulator.Dispose();
        }
    }

    private async Task WaitForGameSurfaceAsync(string serial,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var pid = await ProcessRunner.CaptureAsync(paths.AdbExe,
                ["-s", serial, "shell", "pidof", GameInstaller.PackageName], environment,
                cancellationToken, throwOnError: false);
            if (!string.IsNullOrWhiteSpace(pid))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("게임 화면을 30초 안에 열지 못했습니다.");
    }

    private static async Task WaitForFrontendWindowAsync(Process frontend,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frontend.Refresh();
            if (frontend.HasExited)
                throw new InvalidOperationException(
                    "게임 화면 프로그램이 창을 표시하기 전에 종료됐습니다. GPU 드라이버와 게임 실행 상태를 확인해 주세요.");

            var window = frontend.MainWindowHandle;
            if (window != nint.Zero && IsWindowVisible(window))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException(
            "게임 화면이 30초 안에 표시되지 않았습니다. GPU 드라이버와 게임 실행 상태를 확인해 주세요.");
    }

    private async Task EnsureAccelerationAsync(CancellationToken cancellationToken)
    {
        var checker = Path.Combine(paths.Sdk, "emulator", "emulator-check.exe");
        if (!File.Exists(checker))
            return;
        var result = await ProcessRunner.CaptureAsync(checker, ["accel"], null, cancellationToken, throwOnError: false);
        if (!IsAccelerationAvailable(result))
            throw new VirtualizationUnavailableException(
                "Android 실행에 필요한 가상화 기능이 꺼져 있습니다.\n\n" +
                "아래 순서대로 설정해 주세요.\n\n" +
                "1. 키보드에서 Windows 키 + R을 누릅니다.\n" +
                "2. optionalfeatures를 입력하고 Enter를 누릅니다.\n" +
                "3. 'Windows 하이퍼바이저 플랫폼' 항목을 찾아 체크합니다.\n" +
                "   (영문 Windows에서는 'Windows Hypervisor Platform')\n" +
                "4. '확인'을 누르고 설치가 끝나면 PC를 재부팅합니다.\n" +
                "5. 재부팅 후 포챔스에뮬레이터를 다시 실행합니다.\n\n" +
                "항목이 없거나 재부팅 후에도 이 메시지가 나오면:\n" +
                "• Ctrl + Shift + Esc → '성능' → 'CPU'에서 '가상화: 사용'인지 확인하세요.\n" +
                "• '사용 안 함'이면 BIOS/UEFI에서 Intel VT-x / Intel Virtualization Technology 또는 AMD SVM / AMD-V를 켜야 합니다.\n" +
                "• BIOS 설정 방법은 PC 제조사마다 다르므로 제조사 도움말에서 '가상화 켜기'를 검색하세요.\n\n" +
                "참고: 'Hyper-V' 전체 기능은 설치하지 않아도 됩니다. 회사·학교 PC는 관리자 권한이 필요할 수 있습니다.");
    }

    internal static bool IsAccelerationAvailable(string result) =>
        result.Contains("WHPX", StringComparison.OrdinalIgnoreCase)
        && result.Contains("is installed and usable", StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    private async Task WaitForBootAsync(string serial, Process emulator,
        IReadOnlyDictionary<string, string> environment,
        IProgress<ProgressUpdate> progress, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (emulator.HasExited)
                throw new InvalidOperationException(
                    "Android 에뮬레이터가 부팅 전에 종료됐습니다. 디스크 여유 공간(최소 10GB), GPU 드라이버와 가상화를 확인해 주세요.");
            var output = await ProcessRunner.CaptureAsync(paths.AdbExe,
                ["-s", serial, "shell", "getprop", "sys.boot_completed"], environment,
                cancellationToken, throwOnError: false);
            if (output.Split('\n').Any(line => line.Trim().Equals("1", StringComparison.Ordinal)))
                return;
            progress.Report(new ProgressUpdate("Android 부팅 중… 첫 실행은 조금 더 걸립니다."));
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        throw new TimeoutException("Android가 4분 안에 부팅되지 않았습니다. GPU 드라이버와 가상화를 확인해 주세요.");
    }

    private async Task ApplyGameSettingsAsync(string serial, IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            new[] { "shell", "settings", "put", "global", "window_animation_scale", "0" },
            new[] { "shell", "settings", "put", "global", "transition_animation_scale", "0" },
            new[] { "shell", "settings", "put", "global", "animator_duration_scale", "0" },
            new[] { "shell", "settings", "put", "system", "peak_refresh_rate", "60.0" },
            new[] { "shell", "settings", "put", "system", "min_refresh_rate", "60.0" },
            new[] { "shell", "settings", "put", "secure", "location_mode", "0" },
            new[] { "shell", "settings", "put", "secure", "immersive_mode_confirmations", "confirmed" },
            new[] { "shell", "settings", "put", "global", "wifi_scan_always_enabled", "0" },
            new[] { "shell", "settings", "put", "global", "ble_scan_always_enabled", "0" },
            new[] { "shell", "settings", "put", "global", "mobile_data_always_on", "0" },
            new[] { "shell", "cmd", "power", "set-fixed-performance-mode-enabled", "true" }
        };
        foreach (var command in commands)
        {
            var args = new[] { "-s", serial }.Concat(command);
            await ProcessRunner.CaptureAsync(paths.AdbExe, args, environment, cancellationToken, throwOnError: false);
        }

        string[] unusedPackages =
        [
            "com.android.camera2",
            "com.google.android.apps.maps",
            "com.google.android.apps.messaging",
            "com.google.android.apps.photos",
            "com.google.android.apps.wellbeing",
            "com.google.android.apps.youtube.music",
            "com.google.android.contacts",
            "com.google.android.deskclock",
            "com.google.android.dialer",
            "com.google.android.youtube"
        ];
        foreach (var package in unusedPackages)
        {
            await ProcessRunner.CaptureAsync(paths.AdbExe,
                ["-s", serial, "shell", "pm", "disable-user", "--user", "0", package],
                environment, cancellationToken, throwOnError: false);
        }
    }

    private static int FindFreeEmulatorPort()
    {
        var used = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port).ToHashSet();
        for (var port = 5554; port <= 5584; port += 2)
            if (!used.Contains(port) && !used.Contains(port + 1))
                return port;
        throw new InvalidOperationException("사용 가능한 Android 에뮬레이터 포트가 없습니다.");
    }
}
