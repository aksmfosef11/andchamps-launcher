namespace AndChamps;

internal sealed class AppPaths
{
    public string Root { get; }
    public string Sdk { get; }
    public string Downloads { get; }
    public string AvdHome { get; }
    public string AvdDirectory { get; }
    public string EmulatorExe => Path.Combine(Sdk, "emulator", "emulator.exe");
    public string AdbExe => Path.Combine(Sdk, "platform-tools", "adb.exe");
    public string SystemImage => Path.Combine(Sdk, "system-images", "android-36", "google_apis_playstore", "x86_64");

    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride
            ?? Environment.GetEnvironmentVariable("ANDCHAMPS_RUNTIME_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndChamps");
        Sdk = Path.Combine(Root, "sdk");
        Downloads = Path.Combine(Root, "downloads");
        AvdHome = Path.Combine(Root, "avd");
        AvdDirectory = Path.Combine(AvdHome, "AndChamps36.avd");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Sdk);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(AvdHome);
    }
}
