# 포챔스에뮬레이터 (AndChamps Launcher)

[![CI](https://github.com/aksmfosef11/andchamps-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/aksmfosef11/andchamps-launcher/actions/workflows/ci.yml)

사용자가 직접 준비한 정식 Android 게임 패키지를 Windows에서 실행하도록 돕는
오픈 소스 전용 런처입니다. 범용 앱플레이어를 포함하지 않고 Google Android
Emulator의 WHPX/gfxstream 엔진, 최소 AVD와 scrcpy 게임 창을 사용합니다.

> 이 프로젝트는 비공식 커뮤니티 프로젝트입니다. Pokémon, Pokémon Champions,
> Google, Android, Genymobile 또는 각 권리자와 제휴·후원·승인 관계가 없습니다.
> 게임 파일, 로고, 이미지, 계정 및 저장 데이터는 저장소와 릴리스에 포함하지
> 않습니다. 사용자는 본인이 이용·설치할 권한이 있는 패키지만 사용해야 합니다.

## 주요 기능

- 설치 버튼을 누르기 전에는 Android 구성 요소를 내려받지 않습니다.
- Android SDK 라이선스 동의 후 Emulator, platform-tools와 시스템 이미지를
  Google 공식 저장소에서 직접 내려받습니다.
- scrcpy 4.1은 프로젝트의 공식 GitHub 릴리스에서 직접 내려받고 SHA-256으로
  검증합니다.
- APK/APKS/APKM/XAPK 또는 여러 split APK를 사용자가 직접 선택해 설치합니다.
- Android 홈과 Emulator 프레임을 숨기고 1280×720 게임 창과 오디오만 표시합니다.
- 기본값은 60Hz, GPU host 가속, 5 vCPU, 4GB RAM입니다.
- `데이터 제거`는 게임 앱을 유지하고 로그인·설정·저장 데이터를 초기화합니다.
- `전체 제거`는 앱 전용 런타임 디렉터리의 게임, AVD, SDK와 다운로드만 제거합니다.

첫 설치 다운로드는 약 2.3GB이며 설치 후 약 4~6GB가 필요합니다. 배포 파일에는
Android Studio, JDK, Android SDK/Emulator, Google Play 시스템 이미지와 scrcpy를
넣지 않습니다. 다운로드 압축 파일은 설치 직후 삭제합니다.

## 호환성과 제한

현재 구성은 Android 16 Google Play x86_64 이미지의 ARM64 변환층을 활용합니다.
개발 환경에서는 ARM64 split 패키지, 5 vCPU, 4GB RAM으로 타이틀 화면 진입까지
확인했습니다. PC 사양, GPU 드라이버, 게임 업데이트와 서비스 정책에 따라 결과는
달라질 수 있습니다.

이 런처는 Google Play 로그인, Play Integrity 또는 게임의 에뮬레이터 정책을
우회하지 않습니다. 게임이나 Google이 실행을 허용하지 않으면 런처가 이를
회피하지 않습니다. 최초 언어 선택, 이용약관 동의와 로그인은 사용자가 직접
진행합니다.

## 다운로드와 진위 확인

[GitHub Releases](https://github.com/aksmfosef11/andchamps-launcher/releases)의
`andchamps-launcher-vX.Y.Z-win-x64.zip`을 받습니다. 각 릴리스는 태그가 가리키는
커밋을 GitHub Actions의 깨끗한 Windows 환경에서 빌드합니다.

1. ZIP과 `SHA256SUMS.txt`를 같은 폴더에 받습니다.
2. PowerShell에서 ZIP의 해시를 계산해 텍스트 파일의 값과 비교합니다.

```powershell
Get-FileHash .\andchamps-launcher-v0.1.0-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

ZIP 내부 `build-manifest.json`에는 소스 저장소, 정확한 커밋 SHA, .NET SDK 버전과
각 파일의 SHA-256이 기록됩니다. 저장소를 받은 개발자는 다음 명령으로 매니페스트와
파일을 다시 검증할 수 있습니다.

```powershell
.\scripts\Verify-Release.ps1 `
  -ArtifactPath .\andchamps-launcher-v0.1.0-win-x64.zip `
  -SourceRevision <release-commit-sha>
```

GitHub의 artifact attestation도 ZIP 해시를 저장소·워크플로·커밋과 연결합니다.
검증 절차는 산출물이 공개된 소스 커밋에서 자동 빌드됐다는 공급망 증거를 제공하지만,
코드 서명 인증서 기반의 Windows Authenticode 서명을 대신하지는 않습니다.

## 소스에서 빌드

요구 사항은 Windows와 `global.json`에 지정된 .NET 8 SDK입니다.

```powershell
dotnet run --project .\tests\AndChamps.SmokeTests\AndChamps.SmokeTests.csproj -c Release
.\scripts\Build.ps1 -Configuration Release
```

결과물:

- `artifacts\win-x64\포챔스에뮬레이터.exe`
- `artifacts\win-x64\build-manifest.json`
- `artifacts\release\andchamps-launcher-v0.1.0-win-x64.zip`
- `artifacts\release\SHA256SUMS.txt`

APK 호환성만 진단하려면 다음과 같이 실행합니다.

```powershell
dotnet run --project .\src\AndChamps\AndChamps.csproj -- `
  --diagnose "C:\path\to\game.apks"
```

개발 PC의 Android SDK를 재사용할 때는 `ANDCHAMPS_RUNTIME_ROOT`를 SDK의 상위
폴더로 지정할 수 있습니다. 일반 배포에서는 설정하지 않습니다.

## 라이선스와 제3자 구성 요소

이 저장소의 자체 소스는 [MIT License](LICENSE)로 배포합니다. .NET, scrcpy,
Android SDK/Emulator와 상표 관련 고지는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)를
확인해 주세요. Android SDK 구성 요소는 저장소나 릴리스가 재배포하지 않으며,
사용자가 현재 Android SDK License Agreement를 확인하고 동의한 뒤 공식 서버에서
직접 받습니다.

게임 패키지·이미지·로고는 제공하지 않습니다. 사용자는 본인이 이용 권한을 가진
파일만 사용하고 각 서비스의 이용약관을 따라야 합니다.
