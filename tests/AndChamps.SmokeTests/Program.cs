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

    Assert(LaunchCoordinator.IsAccelerationAvailable(
        "WHPX(10.0.26200) is installed and usable."), "usable WHPX detection");
    Assert(!LaunchCoordinator.IsAccelerationAvailable(
        "WHPX is not installed or usable."), "unavailable WHPX detection");
    Assert(!LaunchCoordinator.IsAccelerationAvailable(
        "AEHD is installed and usable."), "non-WHPX rejection");

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
