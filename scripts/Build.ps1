param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$SourceRevision = '',
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'artifacts\win-x64'
$releaseOutput = Join-Path $projectRoot 'artifacts\release'

if ([string]::IsNullOrWhiteSpace($SourceRevision)) {
    $SourceRevision = (& git -C $projectRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SourceRevision)) {
        $SourceRevision = 'uncommitted'
    }
}
$SourceRevision = $SourceRevision.Trim()

function Remove-BuildDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolvedRoot = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd('\') + '\'
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "빌드 출력 경로가 프로젝트 외부입니다: $resolvedPath"
    }
    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

Remove-BuildDirectory $output
Remove-BuildDirectory $releaseOutput

dotnet publish (Join-Path $projectRoot 'src\AndChamps\AndChamps.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:SourceRevisionId=$SourceRevision `
    -p:RepositoryCommit=$SourceRevision `
    -p:ContinuousIntegrationBuild=true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    --output $output

foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output -Force
}

$executable = Join-Path $output '포챔스에뮬레이터.exe'
$publishedFiles = @(
    '포챔스에뮬레이터.exe',
    'README.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md'
)
$fileEntries = foreach ($name in $publishedFiles) {
    $path = Join-Path $output $name
    $item = Get-Item -LiteralPath $path
    [ordered]@{
        path = $name
        size = $item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    project = 'andchamps-launcher'
    version = $Version
    sourceRepository = 'https://github.com/aksmfosef11/andchamps-launcher'
    sourceCommit = $SourceRevision
    configuration = $Configuration
    runtime = 'win-x64'
    selfContained = $true
    dotnetSdk = (& dotnet --version).Trim()
    executableProductVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
    files = @($fileEntries)
}
$manifestPath = Join-Path $output 'build-manifest.json'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
    $utf8NoBom)

& (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ArtifactPath $output -SourceRevision $SourceRevision

New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
$archiveName = "andchamps-launcher-v$Version-win-x64.zip"
$archivePath = Join-Path $releaseOutput $archiveName
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    (Join-Path $releaseOutput 'SHA256SUMS.txt'),
    "$archiveHash  $archiveName" + [Environment]::NewLine,
    $utf8NoBom)

Write-Host "완료: $archivePath"
