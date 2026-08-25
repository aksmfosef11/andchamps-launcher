using System.Diagnostics;
using System.Text;

namespace AndChamps;

internal static class ProcessRunner
{
    public static Process Start(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null, bool createWindow = true)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = !createWindow,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment)
                info.Environment[pair.Key] = pair.Value;
        return Process.Start(info) ?? throw new InvalidOperationException($"{Path.GetFileName(executable)} 실행에 실패했습니다.");
    }

    public static async Task<string> CaptureAsync(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken,
        bool throwOnError = true)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment)
                info.Environment[pair.Key] = pair.Value;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{Path.GetFileName(executable)} 실행에 실패했습니다.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        return string.IsNullOrWhiteSpace(error) ? output : $"{output}\n{error}".Trim();
    }
}
