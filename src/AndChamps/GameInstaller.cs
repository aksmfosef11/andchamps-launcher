using System.IO.Compression;

namespace AndChamps;

internal static class GameInstaller
{
    public const string PackageName = "jp.pokemon.pokemonchampions";

    public static async Task<bool> IsInstalledAsync(string adb, string serial,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var output = await ProcessRunner.CaptureAsync(adb,
            ["-s", serial, "shell", "pm", "path", PackageName], environment, cancellationToken, throwOnError: false);
        return output.Contains("package:", StringComparison.Ordinal);
    }

    public static async Task InstallAsync(string adb, string serial, string packagePath,
        IReadOnlyDictionary<string, string> environment, IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new ProgressUpdate("선택한 게임 패키지를 설치하고 있습니다…"));
        var extension = Path.GetExtension(packagePath);
        if (extension.Equals(".apk", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessRunner.CaptureAsync(adb,
                ["-s", serial, "install", "-r", "-i", "com.android.vending", packagePath],
                environment, cancellationToken);
            await CompileForSpeedAsync(adb, serial, environment, cancellationToken);
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), "AndChamps", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(packagePath, temp);
            var allApks = Directory.EnumerateFiles(temp, "*.apk", SearchOption.AllDirectories).ToArray();
            var preferredAbi = allApks.Any(path => HasAbiName(path, "x86_64")) ? "x86_64"
                : allApks.Any(path => HasAbiName(path, "arm64_v8a")) ? "arm64_v8a"
                : null;
            var apks = allApks
                .Where(path => IsCompatibleSplit(path, preferredAbi))
                .OrderByDescending(path => Path.GetFileName(path).StartsWith("base", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (apks.Length == 0)
                throw new InvalidDataException("설치할 APK split을 찾지 못했습니다.");
            var arguments = new List<string>
            {
                "-s", serial, "install-multiple", "-r", "-i", "com.android.vending"
            };
            arguments.AddRange(apks);
            await ProcessRunner.CaptureAsync(adb, arguments, environment, cancellationToken);
            await PushExpansionFilesAsync(adb, serial, temp, environment, cancellationToken);
            await CompileForSpeedAsync(adb, serial, environment, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static bool IsCompatibleSplit(string path, string? preferredAbi)
    {
        var name = Path.GetFileName(path).Replace('-', '_').ToLowerInvariant();
        var knownAbis = new[] { "arm64_v8a", "armeabi_v7a", "x86_64", "x86" };
        var splitAbi = knownAbis.FirstOrDefault(name.Contains);
        return splitAbi is null || preferredAbi is null || splitAbi == preferredAbi;
    }

    private static bool HasAbiName(string path, string abi) =>
        Path.GetFileName(path).Replace('-', '_').Contains(abi, StringComparison.OrdinalIgnoreCase);

    private static async Task PushExpansionFilesAsync(string adb, string serial, string extractedRoot,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var obbFiles = Directory.EnumerateFiles(extractedRoot, "*.obb", SearchOption.AllDirectories).ToArray();
        if (obbFiles.Length == 0)
            return;
        var target = $"/sdcard/Android/obb/{PackageName}";
        await ProcessRunner.CaptureAsync(adb, ["-s", serial, "shell", "mkdir", "-p", target],
            environment, cancellationToken);
        foreach (var obb in obbFiles)
            await ProcessRunner.CaptureAsync(adb, ["-s", serial, "push", obb, target + "/"],
                environment, cancellationToken);
    }

    private static Task CompileForSpeedAsync(string adb, string serial,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken) =>
        ProcessRunner.CaptureAsync(adb,
            ["-s", serial, "shell", "cmd", "package", "compile", "-m", "speed", "-f", PackageName],
            environment, cancellationToken, throwOnError: false);

    public static Task LaunchAsync(string adb, string serial,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken) =>
        ProcessRunner.CaptureAsync(adb,
            ["-s", serial, "shell", "monkey", "-p", PackageName, "-c", "android.intent.category.LAUNCHER", "1"],
            environment, cancellationToken);

    public static Task ClearDataAsync(string adb, string serial,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken) =>
        ProcessRunner.CaptureAsync(adb, ["-s", serial, "shell", "pm", "clear", PackageName],
            environment, cancellationToken);

    public static Task OpenPlayStoreAsync(string adb, string serial,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken) =>
        ProcessRunner.CaptureAsync(adb,
            ["-s",
                serial,
                "shell",
                "am",
                "start",
                "-a",
                "android.intent.action.VIEW",
                "-d",
                $"market://details?id={PackageName}"], environment, cancellationToken);
}
