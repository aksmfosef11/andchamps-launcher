namespace AndChamps;

internal sealed record RuntimePackage(string Name, Version Version, Uri DownloadUri, long Size, string Checksum);

internal sealed record RuntimePlan(RuntimePackage Emulator, RuntimePackage PlatformTools,
    RuntimePackage SystemImage, RuntimePackage Scrcpy)
{
    public long TotalSize => Emulator.Size + PlatformTools.Size + SystemImage.Size + Scrcpy.Size;
}

internal sealed record ProgressUpdate(string Message, double? Fraction = null);

internal sealed record SelectedGamePackage(string Path, bool DeleteAfterInstall = false);

internal enum ApkCompatibility
{
    Missing,
    ManagedOnly,
    X64Native,
    ArmOnly,
    Unknown
}

internal sealed record ApkReport(string? Path, ApkCompatibility Compatibility, IReadOnlyCollection<string> Abis, string Detail);
