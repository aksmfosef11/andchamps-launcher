using System.IO.Compression;
using AndChamps;

var root = Path.Combine(Path.GetTempPath(), "AndChamps.SmokeTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var managed = CreateApk("managed.apk", "assets/payload.bin");
    Assert(ApkInspector.Inspect(managed).Compatibility == ApkCompatibility.ManagedOnly, "managed APK");

    var x64 = CreateApk("x64.apk", "lib/x86_64/libgame.so");
    Assert(ApkInspector.Inspect(x64).Compatibility == ApkCompatibility.X64Native, "x86_64 APK");

    var arm = CreateApk("arm.apk", "lib/arm64-v8a/libgame.so");
    Assert(ApkInspector.Inspect(arm).Compatibility == ApkCompatibility.ArmOnly, "arm64 APK");

    var bundle = Path.Combine(root, "game.apks");
    using (var outer = ZipFile.Open(bundle, ZipArchiveMode.Create))
    {
        var entry = outer.CreateEntry("splits/base.apk");
        await using var output = entry.Open();
        await using var input = File.OpenRead(arm);
        await input.CopyToAsync(output);
    }
    Assert(ApkInspector.Inspect(bundle).Compatibility == ApkCompatibility.ArmOnly, "nested APKS");

    var paths = new AppPaths(Path.Combine(root, "runtime"));
    new AvdManager(paths).EnsureCreated();
    var config = await File.ReadAllTextAsync(Path.Combine(paths.AvdDirectory, "config.ini"));
    Assert(config.Contains("hw.ramSize=4096", StringComparison.Ordinal), "AVD RAM");
    Assert(config.Contains("hw.cpu.ncore=5", StringComparison.Ordinal), "AVD CPU");
    Assert(config.Contains("disk.dataPartition.size=6G", StringComparison.Ordinal), "AVD data partition");
    Assert(config.Contains("hw.lcd.width=1280", StringComparison.Ordinal), "AVD resolution");
    Assert(config.Contains("google_apis_playstore", StringComparison.Ordinal), "Play image");
    Assert(config.Contains("fastboot.forceColdBoot=yes", StringComparison.Ordinal),
        "forced cold boot");
    Assert(config.Contains("fastboot.forceFastBoot=no", StringComparison.Ordinal),
        "Quick Boot disabled in AVD config");

    Assert(LaunchCoordinator.IsAccelerationAvailable(
        "WHPX(10.0.26200) is installed and usable."), "usable WHPX detection");
    Assert(!LaunchCoordinator.IsAccelerationAvailable(
        "WHPX is not installed or usable."), "unavailable WHPX detection");
    Assert(!LaunchCoordinator.IsAccelerationAvailable(
        "AEHD is installed and usable."), "non-WHPX rejection");

    var launchOptions = new LaunchOptions();
    var emulatorArguments = LaunchCoordinator.BuildEmulatorArguments(5554, launchOptions);
    Assert(HasArgumentPair(emulatorArguments, "-vsync-rate", "30"), "default emulator refresh rate");
    Assert(HasArgumentPair(emulatorArguments, "-skin", "1280x720"), "native frontend framebuffer size");
    Assert(emulatorArguments.Contains("-no-snapshot"), "snapshots disabled for game launch");
    Assert(!emulatorArguments.Contains("-no-window"), "native frontend enabled");
    var headlessArguments = LaunchCoordinator.BuildHeadlessEmulatorArguments(5556);
    Assert(headlessArguments.Contains("-no-snapshot"), "snapshots disabled for data management");
    Assert(headlessArguments.Contains("-no-window"), "data management remains headless");
    Assert(NativeEmulatorFrontend.GameWindowTitle == "게임 창 · 포챔스에뮬레이터",
        "native frontend title");
    var gameWindowStyle = NativeEmulatorFrontend.BuildGameWindowStyle(0x00010000);
    Assert((gameWindowStyle & 0x00080000) != 0, "native frontend close button");
    Assert((gameWindowStyle & 0x00020000) != 0, "native frontend minimize button");
    Assert((gameWindowStyle & 0x00010000) == 0, "native frontend maximize button removed");

    Assert(LaunchCoordinator.BuildCancelPlayStoreJobsArguments("emulator-5556").SequenceEqual(
        new[]
        {
            "-s", "emulator-5556", "shell", "cmd", "jobscheduler", "cancel", "-u", "0",
            "com.android.vending"
        }),
        "Play Store background job cancellation command");
    Assert(LaunchCoordinator.PlayStorePostFrontendCleanupPasses == 3,
        "Play Store post-frontend cleanup passes");
    Assert(LaunchCoordinator.PlayStorePostFrontendCleanupInterval == TimeSpan.FromSeconds(6),
        "Play Store post-frontend cleanup interval");
    Assert(LaunchCoordinator.UnusedAndroidPackages.Contains("com.google.android.googlequicksearchbox"),
        "Google speech/search background package disabled");
    Assert(LaunchCoordinator.UnusedAndroidPackages.Contains("com.google.android.as"),
        "Android System Intelligence background package disabled");
    Assert(!LaunchCoordinator.UnusedAndroidPackages.Contains("com.android.vending"),
        "Play Store remains enabled for licensing");
    Assert(!LaunchCoordinator.UnusedAndroidPackages.Contains("com.google.android.gms"),
        "Google Play services remain enabled");

    var gameSettings = LaunchCoordinator.BuildGameSettingsCommands(launchOptions);
    Assert(gameSettings.Any(command => command.SequenceEqual(
        new[] { "shell", "settings", "put", "system", "peak_refresh_rate", "30.0" })),
        "default Android peak refresh rate");
    Assert(gameSettings.Any(command => command.SequenceEqual(
        new[] { "shell", "settings", "put", "system", "min_refresh_rate", "30.0" })),
        "default Android minimum refresh rate");
    Assert(gameSettings.Any(command => command.SequenceEqual(
        new[] { "shell", "cmd", "audio", "set-volume", "3", "10" })),
        "native frontend media volume normalized");
    Assert(LaunchCoordinator.NativeMediaVolume == 10,
        "native frontend media volume target");

    var removablePaths = new AppPaths(Path.Combine(root, "removable-runtime"));
    removablePaths.EnsureDirectories();
    await File.WriteAllTextAsync(Path.Combine(removablePaths.Root, "android-sdk-license.accepted"), "test");
    await File.WriteAllTextAsync(Path.Combine(removablePaths.Downloads, "partial.zip"), "test");
    await new RuntimeProvisioner(removablePaths).RemoveAllAsync(
        new Progress<ProgressUpdate>(), CancellationToken.None);
    Assert(!Directory.Exists(removablePaths.Sdk), "full removal SDK");
    Assert(!Directory.Exists(removablePaths.AvdHome), "full removal AVD");
    Assert(!Directory.Exists(removablePaths.Downloads), "full removal downloads");

    Console.WriteLine("AndChamps smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

return;

string CreateApk(string name, string payloadPath)
{
    var path = Path.Combine(root, name);
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    var entry = archive.CreateEntry(payloadPath);
    using var stream = entry.Open();
    stream.WriteByte(0);
    return path;
}

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"Smoke test failed: {name}");
}

static bool HasArgumentPair(IReadOnlyList<string> arguments, string name, string value)
{
    for (var index = 0; index < arguments.Count - 1; index++)
        if (arguments[index] == name && arguments[index + 1] == value)
            return true;
    return false;
}
