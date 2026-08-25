using System.IO.Compression;

namespace AndChamps;

internal static class ApkInspector
{
    private static readonly string[] SupportedExtensions = [".apk", ".apks", ".apkm", ".xapk"];

    public static string? FindGamePackage(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return Path.GetFullPath(explicitPath);

        var environmentPath = Environment.GetEnvironmentVariable("ANDCHAMPS_APK");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
            return Path.GetFullPath(environmentPath);

        var roots = new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "game") };
        return roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            .FirstOrDefault(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
    }

    public static ApkReport Inspect(string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            return new ApkReport(null, ApkCompatibility.Missing, [],
                "APK가 없습니다. 첫 실행에서 Google Play 게임 페이지를 엽니다.");

        try
        {
            var abis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InspectArchive(packagePath, abis, inspectNestedApks: true);
            var compatibility = abis.Count == 0 ? ApkCompatibility.ManagedOnly
                : abis.Contains("x86_64") ? ApkCompatibility.X64Native
                : abis.Contains("arm64-v8a") || abis.Contains("armeabi-v7a") ? ApkCompatibility.ArmOnly
                : ApkCompatibility.Unknown;
            var detail = compatibility switch
            {
                ApkCompatibility.X64Native => "x86_64 네이티브 코드가 있어 WHPX 고속 실행이 가능합니다.",
                ApkCompatibility.ArmOnly => "ARM 전용입니다. Google Play 이미지의 libndk_translation으로 실행하며 네이티브 x86_64보다 CPU 비용이 큽니다.",
                ApkCompatibility.ManagedOnly => "네이티브 라이브러리가 없어 x86_64 이미지에서 실행 가능합니다.",
                _ => "APK ABI를 판정하지 못했습니다."
            };
            return new ApkReport(packagePath, compatibility, abis, detail);
        }
        catch (InvalidDataException ex)
        {
            return new ApkReport(packagePath, ApkCompatibility.Unknown, [], $"APK를 읽지 못했습니다: {ex.Message}");
        }
    }

    private static void InspectArchive(string path, HashSet<string> abis, bool inspectNestedApks)
    {
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0].Equals("lib", StringComparison.OrdinalIgnoreCase))
                abis.Add(parts[1]);

            if (!inspectNestedApks || !entry.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                continue;
            InferAbiFromName(entry.Name, abis);
            if (entry.Length > 256L * 1024 * 1024
                && !entry.Name.Equals("base.apk", StringComparison.OrdinalIgnoreCase))
                continue;
            using var nestedStream = entry.Open();
            using var memory = new MemoryStream();
            nestedStream.CopyTo(memory);
            memory.Position = 0;
            using var nested = new ZipArchive(memory, ZipArchiveMode.Read);
            foreach (var nestedEntry in nested.Entries)
            {
                var nestedParts = nestedEntry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (nestedParts.Length >= 3 && nestedParts[0].Equals("lib", StringComparison.OrdinalIgnoreCase))
                    abis.Add(nestedParts[1]);
            }
        }
    }

    private static void InferAbiFromName(string name, HashSet<string> abis)
    {
        var normalized = name.Replace('-', '_');
        if (normalized.Contains("arm64_v8a", StringComparison.OrdinalIgnoreCase))
            abis.Add("arm64-v8a");
        if (normalized.Contains("armeabi_v7a", StringComparison.OrdinalIgnoreCase))
            abis.Add("armeabi-v7a");
        if (normalized.Contains("x86_64", StringComparison.OrdinalIgnoreCase))
            abis.Add("x86_64");
        else if (normalized.Contains("x86", StringComparison.OrdinalIgnoreCase))
            abis.Add("x86");
    }
}
