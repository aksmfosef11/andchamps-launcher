param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath,
    [string]$SourceRevision = ''
)

$ErrorActionPreference = 'Stop'
$resolvedArtifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
$temporary = $null

try {
    if (Test-Path -LiteralPath $resolvedArtifact -PathType Leaf) {
        if ([IO.Path]::GetExtension($resolvedArtifact) -ne '.zip') {
            throw '검증 대상 파일은 ZIP이어야 합니다.'
        }
        $temporary = Join-Path ([IO.Path]::GetTempPath()) ("AndChamps.Verify." + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporary | Out-Null
        Expand-Archive -LiteralPath $resolvedArtifact -DestinationPath $temporary
        $artifactDirectory = $temporary
    } else {
        $artifactDirectory = $resolvedArtifact
    }

    $manifestPath = Join-Path $artifactDirectory 'build-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'build-manifest.json이 없습니다.'
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.project -ne 'andchamps-launcher') {
        throw '알 수 없는 빌드 매니페스트입니다.'
    }
    if (-not [string]::IsNullOrWhiteSpace($SourceRevision) -and
        $manifest.sourceCommit -ne $SourceRevision) {
        throw "소스 커밋 불일치: expected=$SourceRevision actual=$($manifest.sourceCommit)"
    }

    foreach ($file in $manifest.files) {
        $relative = [string]$file.path
        if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "안전하지 않은 매니페스트 경로입니다: $relative"
        }
        $path = Join-Path $artifactDirectory $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "릴리스 파일이 없습니다: $relative"
        }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$file.sha256).ToLowerInvariant()) {
            throw "SHA-256 불일치: $relative"
        }
        if ((Get-Item -LiteralPath $path).Length -ne [long]$file.size) {
            throw "파일 크기 불일치: $relative"
        }
    }

    $exe = Join-Path $artifactDirectory '포챔스에뮬레이터.exe'
    $productVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    if (-not [string]::IsNullOrWhiteSpace($manifest.sourceCommit) -and
        $manifest.sourceCommit -ne 'uncommitted' -and
        -not $productVersion.Contains([string]$manifest.sourceCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "실행 파일 버전 정보에 소스 커밋이 없습니다: $productVersion"
    }

    Write-Host "검증 완료: source=$($manifest.sourceCommit) sha256/files=$($manifest.files.Count)"
} finally {
    if ($null -ne $temporary -and (Test-Path -LiteralPath $temporary)) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
