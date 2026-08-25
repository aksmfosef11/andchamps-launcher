# Security

보안 취약점은 공개 이슈에 민감한 세부정보를 올리지 말고 저장소 소유자의
GitHub 프로필을 통해 비공개로 알려 주세요.

공식 릴리스는 태그가 가리키는 커밋을 GitHub Actions에서 빌드합니다. 릴리스의
`SHA256SUMS.txt`, `build-manifest.json` 및 GitHub artifact attestation으로 파일과
소스 커밋의 연결을 확인할 수 있습니다. 서명되지 않은 제3자 재배포본은 지원하지
않습니다.
