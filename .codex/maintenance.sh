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
ensure_task "maintenance"

# As in setup.sh: the lines above are bootstrap, the line below is the build.
# `task verify` restores, builds, and tests against the authoritative target,
# replacing the hand-rolled solution-discovery loop this script used to carry.
echo "[maintenance] task verify"
task verify

echo "[maintenance] Complete"
