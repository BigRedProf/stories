# Script Directory

This directory contains only the genuinely complex, multi-step scripts for this
repository. The simple verbs (`restore`, `build`, `test`, `verify`, `clean`)
are defined directly in `Taskfile.yml` — Task is the orchestration layer, and
the repository does not maintain a general-purpose PowerShell build framework.

## Scripts

| Script         | Invoked by     | Purpose                                                                    |
| -------------- | -------------- | -------------------------------------------------------------------------- |
| `common.ps1`   | (dot-sourced)  | Shared helpers: `Write-Step`, `Invoke-Checked`, `Test-CommandExists`, `Get-RepoRoot`, `Test-DotEnvEncoding` |
| `doctor.ps1`   | `task doctor`  | Toolchain diagnostics; checks Task, .NET, Docker, `.env` encoding           |
| `image.ps1`    | `task image`   | Build the Api container image (always runs `docker build`)                  |
| `publish.ps1`  | `task publish` | Tag and push the already-built Api image to ghcr.io                         |

## Two different artifacts

This repository produces two things that are easy to confuse:

| Artifact                    | Built by     | Published by                              |
| --------------------------- | ------------ | ----------------------------------------- |
| `BigRedProf.Stories.*` NuGet packages | `task pack` (locally) | CI, on a push to `main`          |
| `bigredprofstoriesapi` image          | `task image`          | `task publish -- <tag>` (ghcr.io) |

`task pack` deliberately cannot push. Package publishing stays in
`.github/workflows/dotnet.yml`, so nothing local can release a package by
accident. Digihouse also builds and publishes the Api image as part of its own
release, because a digihouse deployment is a five-image unit at one tag.

## Conventions

- These scripts are invoked by Task in their own `pwsh -File` process, so a
  thrown error propagates as a non-zero exit code. Do not chain them in-process.
- No sentinel/up-to-date files for `image`: always invoke the real build and let
  BuildKit decide what to rebuild.
- Paths are resolved from `Get-RepoRoot`, never hard-coded.
- Environment comes from the `.env` files, which Task loads before invoking a
  script. Scripts do not load `.env` themselves.
- Secrets (`GITHUB_PAT_PACKAGE_REGISTRY`, used by `publish` for ghcr.io) come
  from the machine environment or `.env.local`, never from the committed `.env`.
  The image build itself needs no secret: it restores from nuget.org through the
  committed `NuGet.Config`.

## The .env files MUST be UTF-8

Task's dotenv parser reads UTF-8 only, and on a malformed file its error message
**echoes the file's contents** — so a UTF-16 `.env.local` leaks whatever secret
it holds into the console and any captured log.

This is easy to hit by accident: **Windows PowerShell 5.1's `>` and `>>`
redirection writes UTF-16**. Use pwsh 7, or write the file explicitly:

```powershell
[IO.File]::WriteAllText('.env.local', "KEY=value`n", (New-Object Text.UTF8Encoding $false))
```

Because Task parses these files at startup, it fails *before* it can run any
task — so `task doctor` cannot diagnose this. Run doctor directly instead:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File script/doctor.ps1
```

That is the escape hatch for a repository whose `.env` files stop Task running
at all. `Test-DotEnvEncoding` in `common.ps1` implements the check.

## Common Utilities

`common.ps1` is intentionally versioned per-repository. The canonical source
lives at:

```text
foundation/templates/dotnet/script/common.ps1
```
