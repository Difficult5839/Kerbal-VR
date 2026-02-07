#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

KSP_ROOT="${1:-${KSP_ROOT:-}}"
CONFIGURATION="${CONFIGURATION:-Release}"
CKAN_SHIM_DIR=""

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

cleanup() {
  if [[ -n "${CKAN_SHIM_DIR}" && -d "${CKAN_SHIM_DIR}" ]]; then
    rm -rf "${CKAN_SHIM_DIR}"
  fi
}
trap cleanup EXIT

escape_xml() {
  local value="$1"
  value="${value//&/&amp;}"
  value="${value//</&lt;}"
  value="${value//>/&gt;}"
  value="${value//\"/&quot;}"
  value="${value//\'/&apos;}"
  printf '%s' "${value}"
}

detect_managed_relative_path() {
  if [[ -f "${KSP_ROOT}/KSP_Data/Managed/Assembly-CSharp.dll" ]]; then
    echo "KSP_Data/Managed"
    return 0
  fi
  if [[ -f "${KSP_ROOT}/KSP_x64_Data/Managed/Assembly-CSharp.dll" ]]; then
    echo "KSP_x64_Data/Managed"
    return 0
  fi
  if [[ -f "${KSP_ROOT}/KSP.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll" ]]; then
    echo "KSP.app/Contents/Resources/Data/Managed"
    return 0
  fi
  return 1
}

ensure_ckan_command() {
  if command -v ckan >/dev/null 2>&1; then
    return 0
  fi

  echo "[kvr-build] WARNING: 'ckan' not found. Skipping CKAN auto-installs." >&2
  echo "[kvr-build] WARNING: Ensure required mods are already installed in ${KSP_ROOT}/GameData." >&2

  CKAN_SHIM_DIR="$(mktemp -d)"
  cat > "${CKAN_SHIM_DIR}/ckan" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "prompt" && "${2:-}" == "--headless" ]]; then
  cat >/dev/null
  exit 0
fi

echo "This KerbalVR build shim only supports: ckan prompt --headless" >&2
exit 1
EOF
  chmod +x "${CKAN_SHIM_DIR}/ckan"
  export PATH="${CKAN_SHIM_DIR}:${PATH}"
}

KSP_ROOT_XML="$(escape_xml "${KSP_ROOT}")"
MANAGED_RELATIVE_PATH="$(detect_managed_relative_path || true)"
if [[ -z "${MANAGED_RELATIVE_PATH}" ]]; then
  echo "[kvr-build] Could not auto-detect Managed assembly path under KSP root." >&2
  echo "[kvr-build] Checked: KSP_Data/Managed, KSP_x64_Data/Managed, KSP.app/Contents/Resources/Data/Managed" >&2
  exit 1
fi
MANAGED_RELATIVE_PATH_XML="$(escape_xml "${MANAGED_RELATIVE_PATH}")"
PROPS_USER_PATH="${REPO_ROOT}/KerbalVR_Mod/KerbalVR_Mod.props.user"

cat > "${PROPS_USER_PATH}" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <KSPRoot>${KSP_ROOT_XML}</KSPRoot>
    <ManagedRelativePath>${MANAGED_RELATIVE_PATH_XML}</ManagedRelativePath>
  </PropertyGroup>
</Project>
EOF

ensure_ckan_command

MSBUILD_PROPS=(
  "/p:Configuration=${CONFIGURATION}"
  "/p:KSPRoot=${KSP_ROOT}"
  "/p:ManagedRelativePath=${MANAGED_RELATIVE_PATH}"
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
