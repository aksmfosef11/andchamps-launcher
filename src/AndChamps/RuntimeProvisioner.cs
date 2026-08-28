using System.IO.Compression;
using System.Security.Cryptography;

namespace AndChamps;

internal sealed class RuntimeProvisioner(AppPaths paths)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromHours(2) };

    public bool IsReady => File.Exists(paths.EmulatorExe)
        && File.Exists(paths.AdbExe)
        && File.Exists(Path.Combine(paths.SystemImage, "system.img"));

    public async Task EnsureAsync(IProgress<ProgressUpdate> progress, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        RepairLegacySystemImageLayout(progress);
        if (IsReady)
            return;

        progress.Report(new ProgressUpdate("최신 Android 런타임 정보를 확인하고 있습니다…"));
        var plan = await new RepositoryClient(_http).ResolveLatestAsync(cancellationToken);
        var packages = new[]
        {
            (Package: plan.Emulator, Ready: File.Exists(paths.EmulatorExe)),
            (Package: plan.PlatformTools, Ready: File.Exists(paths.AdbExe)),
            (Package: plan.SystemImage, Ready: File.Exists(Path.Combine(paths.SystemImage, "system.img")))
        };

        var pendingTotal = packages.Where(item => !item.Ready).Sum(item => item.Package.Size);
        long completed = 0;
        foreach (var item in packages)
        {
            if (item.Ready)
                continue;

            var label = item.Package.Name.StartsWith("system-images", StringComparison.Ordinal)
                ? "Android 16 경량 이미지"
                : item.Package.Name == "emulator" ? "에뮬레이터 엔진"
                : "ADB 도구";
            var archive = Path.Combine(paths.Downloads, Path.GetFileName(item.Package.DownloadUri.LocalPath));
            await DownloadAsync(item.Package, archive, label, completed, pendingTotal, progress, cancellationToken);

            var extractDestination = item.Package.Name.StartsWith("system-images", StringComparison.Ordinal)
                ? Path.GetDirectoryName(paths.SystemImage)!
                : paths.Sdk;
            await ExtractAsync(archive, extractDestination, item.Package, label, completed, pendingTotal,
                progress, cancellationToken);
            try { File.Delete(archive); } catch { }
            completed += item.Package.Size;
        }

        if (!IsReady)
        {
            var missing = new[]
            {
                ("에뮬레이터", paths.EmulatorExe),
                ("ADB", paths.AdbExe),
                ("Android 시스템 이미지", Path.Combine(paths.SystemImage, "system.img"))
            }.Where(item => !File.Exists(item.Item2)).Select(item => item.Item1);
            throw new InvalidDataException(
                $"Android 런타임 설치가 완전하지 않습니다. 누락: {string.Join(", ", missing)}");
        }
    }

    public async Task RemoveAllAsync(IProgress<ProgressUpdate> progress, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(paths.Root).TrimEnd(Path.DirectorySeparatorChar);
        EnsureSafeRemovalRoot(root);
        progress.Report(new ProgressUpdate("실행 중인 Android 도구를 종료하고 있습니다…"));
        if (File.Exists(paths.AdbExe))
            await ProcessRunner.CaptureAsync(paths.AdbExe, ["kill-server"], null,
                cancellationToken, throwOnError: false);

        progress.Report(new ProgressUpdate("게임과 Android 런타임 파일을 제거하고 있습니다…"));
        await Task.Run(() =>
        {
            var rootPrefix = root + Path.DirectorySeparatorChar;
            foreach (var directory in new[] { paths.AvdHome, paths.Downloads, paths.Sdk })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(directory);
                if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"런타임 외부 경로는 제거할 수 없습니다: {target}");
                DeleteDirectoryWithRetries(target, cancellationToken);
            }

            var marker = Path.Combine(root, "android-sdk-license.accepted");
            if (File.Exists(marker))
                File.Delete(marker);
        }, cancellationToken);
    }

    private static void EnsureSafeRemovalRoot(string root)
    {
        var forbidden = new[]
        {
            Path.GetPathRoot(root),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.GetFullPath(path!));
        if (forbidden.Any(path => root.Equals(path.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("안전을 위해 이 위치의 전체 제거는 허용하지 않습니다.");

        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndChamps");
        var marker = Path.Combine(root, "android-sdk-license.accepted");
        if (!root.Equals(Path.GetFullPath(defaultRoot), StringComparison.OrdinalIgnoreCase)
            && !File.Exists(marker))
            throw new InvalidOperationException("사용자 지정 런타임의 설치 표식을 확인할 수 없어 제거를 중단했습니다.");
    }

    private static void DeleteDirectoryWithRetries(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
            return;
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(400);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(400);
            }
        }
    }

    private void RepairLegacySystemImageLayout(IProgress<ProgressUpdate> progress)
    {
        var expectedImage = Path.Combine(paths.SystemImage, "system.img");
        if (File.Exists(expectedImage))
            return;

        var legacyDirectory = Path.Combine(paths.Sdk, "x86_64");
        var legacyImage = Path.Combine(legacyDirectory, "system.img");
        if (!File.Exists(legacyImage) || Directory.Exists(paths.SystemImage))
            return;

        progress.Report(new ProgressUpdate("기존 Android 이미지를 자동 복구하고 있습니다…"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SystemImage)!);
        Directory.Move(legacyDirectory, paths.SystemImage);
    }

    private async Task DownloadAsync(RuntimePackage package, string destination, string label,
        long priorBytes, long totalBytes, IProgress<ProgressUpdate> progress, CancellationToken cancellationToken)
    {
        if (File.Exists(destination) && await VerifyChecksumAsync(destination, package.Checksum, cancellationToken))
            return;

        var partial = destination + ".partial";
        if (File.Exists(partial))
            File.Delete(partial);

        using var response = await _http.GetAsync(package.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            var fraction = totalBytes == 0 ? 0
                : (priorBytes + received * 0.72d) / totalBytes;
            progress.Report(new ProgressUpdate($"{label} 다운로드 중… {received / 1024 / 1024:N0} MB", fraction));
        }

        await output.FlushAsync(cancellationToken);
        output.Close();
        if (!await VerifyChecksumAsync(partial, package.Checksum, cancellationToken))
            throw new InvalidDataException($"{label} 다운로드 검증에 실패했습니다.");
        File.Move(partial, destination, overwrite: true);
    }

    private static Task ExtractAsync(string archivePath, string destination, RuntimePackage package,
        string label, long priorBytes, long totalBytes, IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
            var expandedTotal = Math.Max(1L, entries.Sum(entry => entry.Length));
            var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            long expanded = 0;
            long lastReported = -4L * 1024 * 1024;
            var buffer = new byte[1024 * 1024];

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("압축 파일에 안전하지 않은 경로가 있습니다.");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                    buffer.Length, FileOptions.SequentialScan);
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    expanded += read;
                    if (expanded - lastReported < 4L * 1024 * 1024 && expanded < expandedTotal)
                        continue;
                    lastReported = expanded;
                    var stage = Math.Clamp((double)expanded / expandedTotal, 0, 1);
                    var fraction = totalBytes == 0 ? stage
                        : (priorBytes + package.Size * (0.72d + stage * 0.28d)) / totalBytes;
                    progress.Report(new ProgressUpdate(
                        $"{label} 압축 푸는 중… {stage:P0}", fraction));
                }
            }
        }, cancellationToken);

    private static async Task<bool> VerifyChecksumAsync(string path, string expected,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = expected.Length == 64
            ? Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
            : Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
