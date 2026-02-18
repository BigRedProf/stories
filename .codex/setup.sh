#!/usr/bin/env bash
set -euo pipefail

echo "[setup] BigRedProf.Stories - starting setup"

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

# ----------------------------
# Prereqs
# ----------------------------
if ! command_exists curl; then
	echo "[setup] ERROR: curl not found"
	exit 1
fi

if ! command_exists python3; then
	echo "[setup] ERROR: python3 not found (used to parse global.json)"
	exit 1
fi

# ----------------------------
# Install .NET SDK pinned by global.json
# ----------------------------
SDK_VERSION="$(read_global_json_sdk_version)"
echo "[setup] global.json SDK version: ${SDK_VERSION}"

export DOTNET_ROOT="${HOME}/.dotnet"
export PATH="${DOTNET_ROOT}:${PATH}"

if [ -x "${DOTNET_ROOT}/dotnet" ] && "${DOTNET_ROOT}/dotnet" --list-sdks | grep -q "^${SDK_VERSION}"; then
	echo "[setup] .NET SDK ${SDK_VERSION} already installed"
else
	echo "[setup] Installing .NET SDK ${SDK_VERSION}..."
	curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
	chmod +x /tmp/dotnet-install.sh
	/tmp/dotnet-install.sh --version "${SDK_VERSION}" --install-dir "${DOTNET_ROOT}"
fi

echo "[setup] dotnet --info"
dotnet --info

# ----------------------------
# Persist dotnet for future shells (Codex task phase)
# ----------------------------
echo "[setup] Persisting DOTNET_ROOT and PATH for future shells..."
cat >/etc/profile.d/dotnet.sh <<'EOF'
export DOTNET_ROOT="/root/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
EOF
chmod 644 /etc/profile.d/dotnet.sh

echo "[setup] Making dotnet available globally..."
ln -sf /root/.dotnet/dotnet /usr/local/bin/dotnet

# ----------------------------
# Speed up restores across runs
# ----------------------------
export NUGET_PACKAGES="${HOME}/.nuget/packages"
mkdir -p "${NUGET_PACKAGES}"

# ----------------------------
# Register BigRedProf NuGet registry (GitHub Packages)
# ----------------------------
echo "[setup] Registering BigRedProf NuGet registry on GitHub Packages..."

if [ -z "${GITHUB_PAT_PACKAGE_REGISTRY:-}" ]; then
	echo "[setup] ERROR: GITHUB_PAT_PACKAGE_REGISTRY is required to restore private BigRedProf packages."
	echo "[setup] Add it as a Codex environment secret."
	exit 1
fi

dotnet nuget remove source "GitHub.BigRedProf" >/dev/null 2>&1 || true

dotnet nuget add source "https://nuget.pkg.github.com/BigRedProf/index.json" \
	--name "GitHub.BigRedProf" \
	--username "BigRedProf" \
	--password "${GITHUB_PAT_PACKAGE_REGISTRY}" \
	--store-password-in-clear-text \
	>/dev/null

# ----------------------------
# Install dotnet tools
# ----------------------------
if [ -f ".config/dotnet-tools.json" ]; then
	echo "[setup] Restoring local dotnet tools..."
	dotnet tool restore
fi

# ----------------------------
# Restore + Build
# ----------------------------
echo "[setup] Discovering solutions/projects..."

mapfile -t solutions < <(find . -maxdepth 6 -name "*.sln" -print | sort)

if [ "${#solutions[@]}" -gt 0 ]; then
	echo "[setup] Found ${#solutions[@]} solution(s)"
	for sln in "${solutions[@]}"; do
		echo "[setup] dotnet restore ${sln}"
		dotnet restore "${sln}"
	done

	for sln in "${solutions[@]}"; do
		echo "[setup] dotnet build ${sln} (Release)"
		dotnet build "${sln}" -c Release --no-restore
	done
else
	mapfile -t projects < <(find . -maxdepth 8 -name "*.csproj" -print | sort)
	if [ "${#projects[@]}" -eq 0 ]; then
		echo "[setup] ERROR: No .sln or .csproj found"
		exit 1
	fi

	echo "[setup] No solutions found; restoring/building ${#projects[@]} project(s)"
	for csproj in "${projects[@]}"; do
		echo "[setup] dotnet restore ${csproj}"
		dotnet restore "${csproj}"
	done

	for csproj in "${projects[@]}"; do
		echo "[setup] dotnet build ${csproj} (Release)"
		dotnet build "${csproj}" -c Release --no-restore
	done
fi

echo "[setup] Complete"
