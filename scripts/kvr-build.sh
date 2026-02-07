#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

KSP_ROOT="${1:-${KSP_ROOT:-}}"
CONFIGURATION="${CONFIGURATION:-Release}"

if [[ -z "${KSP_ROOT}" ]]; then
  echo "Usage: kvr-build.sh <KSP_ROOT>" >&2
  exit 1
fi

if [[ ! -d "${KSP_ROOT}/GameData" ]]; then
  echo "KSP root looks invalid: ${KSP_ROOT}" >&2
  echo "Expected to find: ${KSP_ROOT}/GameData" >&2
  exit 1
fi

if ! command -v msbuild >/dev/null 2>&1; then
  echo "msbuild not found. Run this inside 'nix develop'." >&2
  exit 1
fi

escape_xml() {
  local value="$1"
  value="${value//&/&amp;}"
  value="${value//</&lt;}"
  value="${value//>/&gt;}"
  value="${value//\"/&quot;}"
  value="${value//\'/&apos;}"
  printf '%s' "${value}"
}

KSP_ROOT_XML="$(escape_xml "${KSP_ROOT}")"
PROPS_USER_PATH="${REPO_ROOT}/KerbalVR_Mod/KerbalVR_Mod.props.user"

cat > "${PROPS_USER_PATH}" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <KSPRoot>${KSP_ROOT_XML}</KSPRoot>
  </PropertyGroup>
</Project>
EOF

MSBUILD_PROPS=(
  "/p:Configuration=${CONFIGURATION}"
  "/p:KSPRoot=${KSP_ROOT}"
  "/p:RepoRootPath=${REPO_ROOT}"
  "/p:BinariesOutputRelativePath=KerbalVR_Mod\\GameData\\KerbalVR\\Plugins"
)

echo "[kvr-build] Building KerbalVR.csproj (${CONFIGURATION})"
msbuild "${REPO_ROOT}/KerbalVR_Mod/KerbalVR/KerbalVR.csproj" /t:Restore,Build "${MSBUILD_PROPS[@]}"

echo "[kvr-build] Building KerbalVR-InstallCheck.csproj (${CONFIGURATION})"
msbuild "${REPO_ROOT}/KerbalVR_Mod/KerbalVR-InstallCheck/KerbalVR-InstallCheck.csproj" /t:Restore,Build "${MSBUILD_PROPS[@]}"

KVR_DLL="${REPO_ROOT}/KerbalVR_Mod/GameData/KerbalVR/Plugins/KerbalVR.dll"
INSTALLCHECK_DLL="${REPO_ROOT}/KerbalVR_Mod/GameData/KerbalVR/Plugins/KerbalVR-InstallCheck.dll"
STEAMVR_DLL="${REPO_ROOT}/KerbalVR_Mod/GameData/KerbalVR/Plugins/SteamVR.dll"

for dll in "${KVR_DLL}" "${INSTALLCHECK_DLL}" "${STEAMVR_DLL}"; do
  if [[ ! -f "${dll}" ]]; then
    echo "[kvr-build] Expected output missing: ${dll}" >&2
    exit 1
  fi
done

echo "[kvr-build] Done"
echo "  ${KVR_DLL}"
echo "  ${INSTALLCHECK_DLL}"
echo "  ${STEAMVR_DLL}"
