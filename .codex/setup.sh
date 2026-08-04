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
ensure_task "setup"

# Everything above is genuine BOOTSTRAP -- it makes dotnet and task exist, and
# authenticates the private NuGet feed. Task cannot do any of it, because it is
# what allows Task to run at all.
#
# The build itself is not bootstrap, so it is not duplicated here. `task verify`
# is the same command developers and agents run, against the authoritative
# target in Taskfile.yml, and it restores dotnet tools as part of its graph.
echo "[setup] task verify"
task verify

echo "[setup] Complete"
