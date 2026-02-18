#!/usr/bin/env bash
set -euo pipefail

echo "[maintenance] BigRedProf.Stories - starting maintenance"

# ----------------------------
# Helpers
# ----------------------------
command_exists() {
	command -v "$1" >/dev/null 2>&1
}

read_global_json_sdk_version() {
	python3 - <<'PY'
import json
with open("global.json", "r", encoding="utf-8") as f:
	data = json.load(f)
sdk = (data.get("sdk") or {})
version = sdk.get("version")
if not version:
	raise SystemExit(1)
print(version)
PY
}

install_dotnet_if_needed() {
	local sdk_version="$1"

	export DOTNET_ROOT="${HOME}/.dotnet"
	export PATH="${DOTNET_ROOT}:${PATH}"

	if [ -x "${DOTNET_ROOT}/dotnet" ] && "${DOTNET_ROOT}/dotnet" --list-sdks | grep -q "^${sdk_version}"; then
		echo "[maintenance] .NET SDK ${sdk_version} already installed"
		return 0
	fi

	echo "[maintenance] Installing .NET SDK ${sdk_version}..."
	curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
	chmod +x /tmp/dotnet-install.sh
	/tmp/dotnet-install.sh --version "${sdk_version}" --install-dir "${DOTNET_ROOT}"
}

# ----------------------------
# Prereqs
# ----------------------------
if ! command_exists curl; then
	echo "[maintenance] ERROR: curl not found"
	exit 1
fi

if ! command_exists python3; then
	echo "[maintenance] ERROR: python3 not found (used to parse global.json)"
	exit 1
fi

# ----------------------------
# Ensure correct .NET SDK
# ----------------------------
SDK_VERSION="$(read_global_json_sdk_version)"
echo "[maintenance] global.json SDK version: ${SDK_VERSION}"
install_dotnet_if_needed "${SDK_VERSION}"

# ----------------------------
# NuGet cache
# ----------------------------
export NUGET_PACKAGES="${HOME}/.nuget/packages"
mkdir -p "${NUGET_PACKAGES}"

# ----------------------------
# Restore (and optionally build)
# ----------------------------
echo "[maintenance] Discovering solutions/projects..."

mapfile -t solutions < <(find . -maxdepth 6 -name "*.sln" -print | sort)

if [ "${#solutions[@]}" -gt 0 ]; then
	echo "[maintenance] Restoring ${#solutions[@]} solution(s)..."
	for sln in "${solutions[@]}"; do
		echo "[maintenance] dotnet restore ${sln}"
		dotnet restore "${sln}"
	done

	# Optional: a quick build to catch missing refs early.
	echo "[maintenance] Building solutions (Release)..."
	for sln in "${solutions[@]}"; do
		echo "[maintenance] dotnet build ${sln} (Release)"
		dotnet build "${sln}" -c Release --no-restore
	done
else
	mapfile -t projects < <(find . -maxdepth 8 -name "*.csproj" -print | sort)
	if [ "${#projects[@]}" -eq 0 ]; then
		echo "[maintenance] ERROR: No .sln or .csproj found"
		exit 1
	fi

	echo "[maintenance] Restoring ${#projects[@]} project(s)..."
	for csproj in "${projects[@]}"; do
		echo "[maintenance] dotnet restore ${csproj}"
		dotnet restore "${csproj}"
	done

	# Optional: quick build.
	echo "[maintenance] Building projects (Release)..."
	for csproj in "${projects[@]}"; do
		echo "[maintenance] dotnet build ${csproj} (Release)"
		dotnet build "${csproj}" -c Release --no-restore
	done
fi

echo "[maintenance] Complete"
