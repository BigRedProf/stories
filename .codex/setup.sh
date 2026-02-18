#!/usr/bin/env bash
set -euo pipefail

echo "[setup] BigRedProf.Stories - starting setup"

# shellcheck source=/dev/null
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_prereqs "setup"

SDK_VERSION="$(read_global_json_sdk_version)"
echo "[setup] global.json SDK version: ${SDK_VERSION}"

ensure_dotnet_sdk "setup" "${SDK_VERSION}"

echo "[setup] dotnet --info"
dotnet --info

ensure_nuget_cache
ensure_bigredprof_github_nuget_source "setup"
restore_dotnet_tools_if_present "setup"

echo "[setup] Discovering solutions/projects..."

mapfile -t solutions < <(discover_solutions)

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
	mapfile -t projects < <(discover_projects)
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
