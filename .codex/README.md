# Codex Environment – BigRedProf.Stories

## Setup Script

`.codex/setup.sh`

- Installs .NET SDK pinned in `global.json`
- Restores NuGet packages
- Builds solutions/projects in Release

Runs when a fresh environment is created.

---

## Maintenance Script

`.codex/maintenance.sh`

- Ensures correct .NET SDK is present
- Re-runs restore
- Performs a quick Release build

Runs when reusing a cached container.

---

## When To Reset Cache

You must reset the Codex cache if:

- You change the SDK version in `global.json`
- You change setup.sh logic significantly
- You add new system-level dependencies

---

## Local Development

You can run:

    bash .codex/setup.sh

locally on Linux/macOS to simulate the Codex environment.
