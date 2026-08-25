using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AndChamps;

internal static class Diagnostics
{
    public static async Task PrintConsoleReportAsync(string? explicitPackage)
    {
        try
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
            var report = ApkInspector.Inspect(ApkInspector.FindGamePackage(explicitPackage));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var plan = await new RepositoryClient(http).ResolveLatestAsync(CancellationToken.None);
            var lines = new[]
            {
                "포챔스에뮬레이터 진단",
                $"OS: {RuntimeInformation.OSDescription}",
                $"Architecture: {RuntimeInformation.OSArchitecture}",
                $"Logical CPU: {Environment.ProcessorCount}",
                $"Runtime root: {new AppPaths().Root}",
                $"Virtualization firmware enabled: {ReadVirtualizationStatus()}",
                $"Game package: {report.Path ?? "없음"}",
                $"APK compatibility: {report.Compatibility}",
                $"APK ABI: {(report.Abis.Count == 0 ? "없음/미확인" : string.Join(", ", report.Abis))}",
                report.Detail,
                $"Latest emulator: {plan.Emulator.Version}",
                $"Latest platform-tools: {plan.PlatformTools.Version}",
                $"Pinned system image: {plan.SystemImage.Name} r{plan.SystemImage.Version.Major}",
                $"Game window: scrcpy {plan.Scrcpy.Version}",
                $"First download: {plan.TotalSize / 1024d / 1024d:N0} MB"
            };
            var text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            Console.Write(text);
            var reportPath = Path.Combine(Environment.CurrentDirectory, "andchamps-diagnostics.txt");
            File.WriteAllText(reportPath, text, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
    }

    private static string ReadVirtualizationStatus()
    {
        try
        {
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("(Get-CimInstance Win32_Processor).VirtualizationFirmwareEnabled");
            using var process = Process.Start(info);
            if (process is null)
                return "unknown";
            var value = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }
        catch
        {
            return "unknown";
        }
    }
}
