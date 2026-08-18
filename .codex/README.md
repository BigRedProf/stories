# Codex Environment – BigRedProf.Stories

These scripts do **bootstrap only**, then hand off to Task. They make `dotnet`
and `task` exist and authenticate the private NuGet feed — none of which Task
can do for itself, because it is what lets Task run at all. The build is not
duplicated here: both scripts finish with `task verify`, the same command
developers and agents run locally, against the authoritative target in
`Taskfile.yml`.

## Setup Script

`.codex/setup.sh`

- Installs the .NET SDK pinned in `global.json`
- Installs Task (go-task)
- Registers the private BigRedProf NuGet source
- Runs `task verify` (restore, build, test)

Runs when a fresh environment is created.

---

## Maintenance Script

`.codex/maintenance.sh`

- Ensures the correct .NET SDK and Task are present
- Re-registers the NuGet source
- Runs `task verify`

Runs when reusing a cached container.

---

## Required Secrets

None. Every package this repository restores is public on nuget.org, so setup
needs no credentials. This previously required `GITHUB_PAT_PACKAGE_REGISTRY` to
reach GitHub Packages, whose NuGet feed demands authentication even for public
packages.

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

locally on Linux/macOS to simulate the Codex environment. On a normal developer
machine you do not need these scripts at all — just `task verify`.
