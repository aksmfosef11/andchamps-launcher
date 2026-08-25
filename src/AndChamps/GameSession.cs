using System.Diagnostics;

namespace AndChamps;

internal sealed class GameSession(Process emulator, Process frontend, string adb, string serial,
    IReadOnlyDictionary<string, string> environment) : IDisposable
{
    private int _disposed;

    public async Task WaitForExitAsync(CancellationToken cancellationToken, Action? exitDetected = null)
    {
        try
        {
            var frontendExit = frontend.WaitForExitAsync(cancellationToken);
            var emulatorExit = emulator.WaitForExitAsync(cancellationToken);
            await Task.WhenAny(frontendExit, emulatorExit);
            exitDetected?.Invoke();
        }
        finally
        {
            await StopAsync();
        }
    }

    private async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (!frontend.HasExited)
        {
            try { frontend.CloseMainWindow(); } catch { }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await frontend.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                try { frontend.Kill(entireProcessTree: true); } catch { }
            }
        }

        if (!emulator.HasExited)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await ProcessRunner.CaptureAsync(adb, ["-s", serial, "emu", "kill"], environment,
                    timeout.Token, throwOnError: false);
                await emulator.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                try { emulator.Kill(entireProcessTree: true); } catch { }
            }
        }

        frontend.Dispose();
        emulator.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try { if (!frontend.HasExited) frontend.Kill(entireProcessTree: true); } catch { }
        try { if (!emulator.HasExited) emulator.Kill(entireProcessTree: true); } catch { }
        frontend.Dispose();
        emulator.Dispose();
    }
}
