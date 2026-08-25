using System.Globalization;
using System.Xml.Linq;

namespace AndChamps;

internal sealed class RepositoryClient(HttpClient http)
{
    private static readonly Uri MainRepository = new("https://dl.google.com/android/repository/repository2-3.xml");
    private static readonly Uri PlayImagesRepository = new("https://dl.google.com/android/repository/sys-img/google_apis_playstore/sys-img2-3.xml");
    private static readonly Uri MainBase = new("https://dl.google.com/android/repository/");
    private static readonly Uri PlayImagesBase = new("https://dl.google.com/android/repository/sys-img/google_apis_playstore/");
    private static readonly RuntimePackage Scrcpy = new(
        "scrcpy", new Version(4, 1),
        new Uri("https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-win64-v4.1.zip"),
        11_305_298,
        "5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db");

    public async Task<RuntimePlan> ResolveLatestAsync(CancellationToken cancellationToken)
    {
        var mainXml = await http.GetStringAsync(MainRepository, cancellationToken);
        var imageXml = await http.GetStringAsync(PlayImagesRepository, cancellationToken);
        var main = XDocument.Parse(mainXml);
        var images = XDocument.Parse(imageXml);

        var emulator = Resolve(main, "emulator", "windows", MainBase);
        var platformTools = Resolve(main, "platform-tools", "windows", MainBase);
        var systemImage = Resolve(images,
            "system-images;android-36;google_apis_playstore;x86_64", null, PlayImagesBase);

        return new RuntimePlan(emulator, platformTools, systemImage, Scrcpy);
    }

    private static RuntimePackage Resolve(XDocument document, string path, string? hostOs, Uri baseUri)
    {
        var matches = document.Descendants()
            .Where(node => node.Name.LocalName == "remotePackage"
                && string.Equals((string?)node.Attribute("path"), path, StringComparison.Ordinal))
            .Select(node => new { Node = node, Version = ReadVersion(node), IsStable = IsStable(node) })
            .Where(item => item.IsStable)
            .OrderByDescending(item => item.Version)
            .ToArray();

        foreach (var match in matches)
        {
            var archive = match.Node.Descendants()
                .Where(node => node.Name.LocalName == "archive")
                .FirstOrDefault(node => hostOs is null || string.Equals(
                    node.Elements().FirstOrDefault(child => child.Name.LocalName == "host-os")?.Value,
                    hostOs, StringComparison.OrdinalIgnoreCase));
            if (archive is null)
                continue;

            var complete = archive.Elements().FirstOrDefault(node => node.Name.LocalName == "complete")
                ?? throw new InvalidDataException($"{path} 다운로드 정보가 없습니다.");
            var url = ChildValue(complete, "url");
            var size = long.Parse(ChildValue(complete, "size"), CultureInfo.InvariantCulture);
            var checksum = complete.Elements().FirstOrDefault(node => node.Name.LocalName == "checksum")?.Value ?? "";
            return new RuntimePackage(path, match.Version, new Uri(baseUri, url), size, checksum);
        }

        throw new InvalidDataException($"공식 Android 저장소에서 {path} 패키지를 찾지 못했습니다.");
    }

    private static bool IsStable(XElement package)
    {
        var channel = package.Elements().FirstOrDefault(node => node.Name.LocalName == "channelRef");
        return channel is null || string.Equals((string?)channel.Attribute("ref"), "channel-0", StringComparison.Ordinal);
    }

    private static Version ReadVersion(XElement package)
    {
        var revision = package.Elements().First(node => node.Name.LocalName == "revision");
        int Part(string name) => int.TryParse(
            revision.Elements().FirstOrDefault(node => node.Name.LocalName == name)?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        return new Version(Part("major"), Part("minor"), Part("micro"), Part("preview"));
    }

    private static string ChildValue(XElement parent, string localName) =>
        parent.Elements().First(node => node.Name.LocalName == localName).Value;
}
