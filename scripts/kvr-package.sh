#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

KSP_ROOT="${1:-${KSP_ROOT:-}}"
OUTPUT_ZIP="${2:-${REPO_ROOT}/dist/KerbalVR-dev-$(date +%Y%m%d-%H%M%S).zip}"

if [[ -z "${KSP_ROOT}" ]]; then
  echo "Usage: kvr-package.sh <KSP_ROOT> [output-zip]" >&2
  exit 1
fi

"${SCRIPT_DIR}/kvr-build.sh" "${KSP_ROOT}"

mkdir -p "$(dirname "${OUTPUT_ZIP}")"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

cp -a "${REPO_ROOT}/KerbalVR_Mod/GameData" "${TMP_DIR}/GameData"
cp -a "${REPO_ROOT}/KerbalVR_Mod/KSP_x64_Data" "${TMP_DIR}/KSP_x64_Data"

(
  cd "${TMP_DIR}"
  zip -qr "${OUTPUT_ZIP}" "GameData" "KSP_x64_Data"
)

echo "[kvr-package] Wrote ${OUTPUT_ZIP}"
