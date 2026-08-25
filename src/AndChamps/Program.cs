using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AndChamps;

internal static class Program
{
    private const string MutexName = "Local\\AndChamps.Launcher";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--diagnose", StringComparison.OrdinalIgnoreCase))
        {
            NativeConsole.AttachToParent();
            Diagnostics.PrintConsoleReportAsync(args.Skip(1).FirstOrDefault()).GetAwaiter().GetResult();
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("포챔스에뮬레이터가 이미 실행 중입니다.", "포챔스에뮬레이터",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}

internal static class NativeConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    public static void AttachToParent()
    {
        if (!AttachConsole(AttachParentProcess))
            return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }
}
