namespace AndChamps;

internal sealed class AvdManager(AppPaths paths)
{
    public const string Name = "AndChamps36";

    public void EnsureCreated()
    {
        paths.EnsureDirectories();
        Directory.CreateDirectory(paths.AvdDirectory);
        var iniPath = Path.Combine(paths.AvdHome, $"{Name}.ini");
        var systemImageRelative = @"system-images\android-36\google_apis_playstore\x86_64\";
        File.WriteAllText(iniPath, string.Join(Environment.NewLine,
        [
            "avd.ini.encoding=UTF-8",
            $"path={paths.AvdDirectory}",
            "target=android-36"
        ]) + Environment.NewLine);

        var configPath = Path.Combine(paths.AvdDirectory, "config.ini");
        const int cores = 5;
        File.WriteAllText(configPath, string.Join(Environment.NewLine,
        [
            "AvdId=AndChamps36",
            "PlayStore.enabled=true",
            "abi.type=x86_64",
            "avd.ini.displayname=AndChamps36",
            "disk.dataPartition.size=6G",
            "fastboot.chosenSnapshotFile=",
            "fastboot.forceChosenSnapshotBoot=no",
            "fastboot.forceColdBoot=no",
            "fastboot.forceFastBoot=yes",
            "hw.accelerometer=no",
            "hw.audioInput=no",
            "hw.audioOutput=yes",
            "hw.battery=no",
            "hw.camera.back=none",
            "hw.camera.front=none",
            "hw.cpu.arch=x86_64",
            $"hw.cpu.ncore={cores}",
            "hw.dPad=no",
            "hw.gps=no",
            "hw.gpu.enabled=yes",
            "hw.gpu.mode=host",
            "hw.keyboard=yes",
            "hw.lcd.density=240",
            "hw.lcd.height=720",
            "hw.lcd.width=1280",
            "hw.mainKeys=no",
            "hw.ramSize=4096",
            "hw.sdCard=no",
            "hw.sensors.light=no",
            "hw.sensors.magnetic_field=no",
            "hw.sensors.orientation=no",
            "hw.sensors.pressure=no",
            "hw.sensors.proximity=no",
            $"image.sysdir.1={systemImageRelative}",
            "runtime.network.latency=none",
            "runtime.network.speed=full",
            "showDeviceFrame=no",
            "skin.dynamic=yes",
            "skin.name=1280x720",
            "skin.path=_no_skin",
            "tag.display=Google Play",
            "tag.id=google_apis_playstore",
            "vm.heapSize=512"
        ]) + Environment.NewLine);
    }

    public IReadOnlyDictionary<string, string> EnvironmentVariables => new Dictionary<string, string>
    {
        ["ANDROID_SDK_ROOT"] = paths.Sdk,
        ["ANDROID_HOME"] = paths.Sdk,
        ["ANDROID_AVD_HOME"] = paths.AvdHome
    };
}
