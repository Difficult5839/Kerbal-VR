#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

KSP_ROOT="${1:-${KSP_ROOT:-}}"

if [[ -z "${KSP_ROOT}" ]]; then
  echo "Usage: kvr-install.sh <KSP_ROOT>" >&2
  exit 1
fi

if [[ ! -d "${KSP_ROOT}/GameData" ]]; then
  echo "KSP root looks invalid: ${KSP_ROOT}" >&2
  echo "Expected to find: ${KSP_ROOT}/GameData" >&2
  exit 1
fi

"${SCRIPT_DIR}/kvr-build.sh" "${KSP_ROOT}"

mkdir -p "${KSP_ROOT}/GameData"
mkdir -p "${KSP_ROOT}/KSP_x64_Data"

rsync -a "${REPO_ROOT}/KerbalVR_Mod/GameData/KerbalVR" "${KSP_ROOT}/GameData/"
rsync -a "${REPO_ROOT}/KerbalVR_Mod/KSP_x64_Data/" "${KSP_ROOT}/KSP_x64_Data/"

echo "[kvr-install] Installed into ${KSP_ROOT}"
echo "  ${KSP_ROOT}/GameData/KerbalVR"
echo "  ${KSP_ROOT}/KSP_x64_Data"
