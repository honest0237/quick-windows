#!/usr/bin/env bash
# Quick for Windows 릴리스 스크립트.
# CI(windows-build)가 만든 단일 Quick.exe 아티팩트를 받아 SHA-256과 함께 GitHub 릴리스로 배포.
# 자동 업데이트(UpdateService)가 .sha256 자산으로 무결성을 검증하므로 exe와 sha256을 함께 올린다.
#
# 사용법:
#   scripts/release.sh <version> [run_id] [--stable]
#     <version>  예) 0.3.2  (csproj <Version>과 일치해야 함)
#     [run_id]   생략 시 main의 최신 성공한 CI run에서 아티팩트를 받음
#     --stable   생략하면 --prerelease로 배포(기본)
set -euo pipefail

REPO="honest0237/quick-windows"
VERSION="${1:-}"
[ -n "$VERSION" ] || { echo "usage: release.sh <version> [run_id] [--stable]" >&2; exit 1; }
TAG="v${VERSION}"

RUN_ID=""
PRERELEASE="--prerelease"
for arg in "${@:2}"; do
  case "$arg" in
    --stable) PRERELEASE="" ;;
    *[!0-9]*) echo "무시된 인자: $arg" >&2 ;;
    *) RUN_ID="$arg" ;;
  esac
done

if [ -z "$RUN_ID" ]; then
  RUN_ID=$(gh run list --repo "$REPO" --workflow ci.yml --branch main \
             --status success --limit 1 --json databaseId -q '.[0].databaseId')
fi
echo "run_id=$RUN_ID  tag=$TAG  ${PRERELEASE:-(stable)}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
gh run download "$RUN_ID" --repo "$REPO" -D "$WORK"
EXE="$(find "$WORK" -name 'Quick.exe' | head -1)"
[ -n "$EXE" ] || { echo "아티팩트에서 Quick.exe를 찾지 못함" >&2; exit 1; }

# SHA-256 자산 생성 ("<hash>  Quick.exe" 형식 — UpdateService.VerifyAsync가 첫 토큰만 사용)
SHA="${EXE}.sha256"
if command -v sha256sum >/dev/null 2>&1; then
  ( cd "$(dirname "$EXE")" && sha256sum "$(basename "$EXE")" ) > "$SHA"
else
  ( cd "$(dirname "$EXE")" && shasum -a 256 "$(basename "$EXE")" ) > "$SHA"
fi
echo "sha256: $(cat "$SHA")"

gh release create "$TAG" "$EXE" "$SHA" \
  --repo "$REPO" \
  --title "Quick for Windows ${TAG}" \
  ${PRERELEASE} \
  --notes-file - <<EOF
## Quick for Windows ${TAG}

아래 **Quick.exe** 를 내려받아 실행하세요. (서명 없음 → SmartScreen "추가 정보 → 실행")

- 기존 v0.3.2 이상 사용자는 실행 시 이 버전을 감지해 **원클릭 자동 설치**(다운로드→교체→재시작)를 제안합니다.
- \`Quick.exe.sha256\` 은 자동 업데이트가 무결성을 검증하는 데 사용됩니다.
EOF

echo "완료 → https://github.com/${REPO}/releases/tag/${TAG}"
