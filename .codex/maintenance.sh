#!/usr/bin/env bash
set -euo pipefail

echo "[maintenance] BigRedProf.Stories - starting maintenance"

# shellcheck source=/dev/null
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_prereqs "maintenance"

SDK_VERSION="$(read_global_json_sdk_version)"
echo "[maintenance] global.json SDK version: ${SDK_VERSION}"

ensure_dotnet_sdk "maintenance" "${SDK_VERSION}"

ensure_nuget_cache
ensure_bigredprof_github_nuget_source "maintenance"
restore_dotnet_tools_if_present "maintenance"

echo "[maintenance] Discovering solutions/projects..."

mapfile -t solutions < <(discover_solutions)

if [ "${#solutions[@]}" -gt 0 ]; then
	echo "[maintenance] Restoring ${#solutions[@]} solution(s)..."
	for sln in "${solutions[@]}"; do
		echo "[maintenance] dotnet restore ${sln}"
		dotnet restore "${sln}"
	done

	echo "[maintenance] Building solutions (Release)..."
	for sln in "${solutions[@]}"; do
		echo "[maintenance] dotnet build ${sln} (Release)"
		dotnet build "${sln}" -c Release --no-restore
	done
else
	mapfile -t projects < <(discover_projects)
	if [ "${#projects[@]}" -eq 0 ]; then
		echo "[maintenance] ERROR: No .sln or .csproj found"
		exit 1
	fi

	echo "[maintenance] Restoring ${#projects[@]} project(s)..."
	for csproj in "${projects[@]}"; do
		echo "[maintenance] dotnet restore ${csproj}"
		dotnet restore "${csproj}"
	done

	echo "[maintenance] Building projects (Release)..."
	for csproj in "${projects[@]}"; do
		echo "[maintenance] dotnet build ${csproj} (Release)"
		dotnet build "${csproj}" -c Release --no-restore
	done
fi

echo "[maintenance] Complete"
