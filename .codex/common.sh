#!/usr/bin/env bash
set -euo pipefail

# Common utilities for BigRedProf Codex scripts.
# Intended to be sourced by setup.sh and maintenance.sh.

command_exists() {
	command -v "$1" >/dev/null 2>&1
}

require_prereqs() {
	local prefix="$1"

	if ! command_exists curl; then
		echo "[${prefix}] ERROR: curl not found"
		exit 1
	fi

	if ! command_exists python3; then
		echo "[${prefix}] ERROR: python3 not found (used to parse global.json)"
		exit 1
	fi
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

ensure_dotnet_sdk() {
	local prefix="$1"
	local sdk_version="$2"

	export DOTNET_ROOT="${HOME}/.dotnet"
	export PATH="${DOTNET_ROOT}:${PATH}"

	if [ -x "${DOTNET_ROOT}/dotnet" ] && "${DOTNET_ROOT}/dotnet" --list-sdks | grep -q "^${sdk_version}"; then
		echo "[${prefix}] .NET SDK ${sdk_version} already installed"
	else
		echo "[${prefix}] Installing .NET SDK ${sdk_version}..."
		curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
		chmod +x /tmp/dotnet-install.sh
		/tmp/dotnet-install.sh --version "${sdk_version}" --install-dir "${DOTNET_ROOT}"
	fi

	echo "[${prefix}] Persisting DOTNET_ROOT and PATH for future shells..."
	cat >/etc/profile.d/dotnet.sh <<'EOF'
export DOTNET_ROOT="/root/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
EOF
	chmod 644 /etc/profile.d/dotnet.sh

	echo "[${prefix}] Making dotnet available globally..."
	ln -sf /root/.dotnet/dotnet /usr/local/bin/dotnet
}

ensure_nuget_cache() {
	export NUGET_PACKAGES="${HOME}/.nuget/packages"
	mkdir -p "${NUGET_PACKAGES}"
}

# Task (go-task) is this repository's orchestration layer, so the Codex
# environment needs it for the same reason it needs the .NET SDK: `task verify`
# is the build. Installed the same way as the SDK above -- fetch the official
# installer, put the binary somewhere already on PATH.
#
# Solution/project discovery used to live here, and setup.sh/maintenance.sh
# hand-rolled their own restore+build loops over the results. That is exactly
# the duplicated orchestration Task replaces: the authoritative target is now
# TARGET in Taskfile.yml, so these scripts call `task verify` instead.
ensure_task() {
	local prefix="$1"

	if command_exists task; then
		echo "[${prefix}] Task already installed: $(task --version)"
		return
	fi

	echo "[${prefix}] Installing Task (go-task)..."
	curl -fsSL https://taskfile.dev/install.sh -o /tmp/task-install.sh
	chmod +x /tmp/task-install.sh
	/tmp/task-install.sh -d -b /usr/local/bin
}
