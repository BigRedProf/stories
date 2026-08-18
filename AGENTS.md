# Agent Instructions – BigRedProf.Stories

This repository contains the BigRedProf.Stories library.

Stories capture a series of things over time using an append-only model. This is
an event-sourced style library where determinism, clarity, and backward
compatibility matter.

This repository follows the BigRedProf development environment conventions. This
file is the single source of shared instructions for agents and contributors;
`CLAUDE.md` imports it via `@AGENTS.md`.

---

## Authoritative Coding Standards

All formatting, organization, naming, nullability, defensive programming, and
structural code-style rules are defined in `CODING_GUIDELINES.md`, which is the
authoritative source of truth. If anything here conflicts with it, follow
`CODING_GUIDELINES.md`.

---

## Standard Commands

This repository is driven by [Task](https://taskfile.dev). Task is the
orchestration layer and loads the layered environment (`.env.local` then `.env`)
on every invocation, so no shell setup is needed — commands work in a fresh
process for humans and agents alike.

```powershell
task build      # fast inner loop (restore once, then build)
task test       # .NET unit tests, no rebuild
task test:powershell  # the PowerShell module's own tests
task verify     # everything required before merging — the success criterion
task clean
task doctor     # toolchain/version diagnostics
```

List everything with `task --list`.

`verify` is the canonical success criterion. It is fast by design (build, the
.NET unit tests, and the PowerShell module's tests). Slow, on-demand work is separate and intentionally excluded from
the inner loop:

```powershell
task image             # build the Api container image (SLOW)
task publish -- <tag>  # tag and push the Api image to ghcr.io
task pack              # produce the NuGet packages locally
task module            # stage the PowerShell module into artifacts/module
```

Stories specifics:

- The build **target** is `src/stories.sln`. Note the solution lives under
  `src/`, not at the repository root.
- **There are two test suites, and `verify` runs both.** `task test` is
  `dotnet test` over `src/StoriesCli.Test`. `task test:powershell` runs
  `tests/PowerShell.Test`, because the module's behaviour lives in a manifest and
  a cmdlet surface that `dotnet test` cannot see at all. New PowerShell tests go
  under `tests/`; the .NET test project is still under `src/`, a known deviation
  from `REPO_CONVENTIONS.md` tracked by #4.
- **Two things ship from here, on independent version lines.** The four
  `BigRedProf.Stories.*` NuGet packages release on a `v*` tag to nuget.org; the
  `BigRedProf.Stories` PowerShell module releases on a `psmodule-v*` tag to the
  PowerShell Gallery. The prefixes are what keep them independent, so releasing
  one never drags the other along. Neither can be released locally: `task pack`
  and `task module` only build.
- **Reading a story from the command line is the module's job, not the CLI's.**
  `Get-Story` and `Watch-Story` replaced the CLI's `listen` verb, which is gone.
  The CLI keeps the tape verbs (`backup`, `restore`, `verify`, `inspect`).
- `task publish` needs `GITHUB_PAT_PACKAGE_REGISTRY` in the environment (or
  `.env.local`) to sign in to **ghcr.io**. It is a secret and never goes in
  `.env`. `task image` no longer needs it: the image restores from nuget.org,
  where every BigRedProf package is public, via the committed `NuGet.Config`.
- The module is a **binary** PowerShell module, which has one trap worth
  knowing: PowerShell loads it into the default assembly load context, which
  probes *pwsh's* directory rather than the module's. A model assembly passed to
  `-ModelAssembly` therefore cannot find its own dependencies unless the module
  installs a resolver, and the failure surfaces during pack rat registration
  naming the *version* the model assembly was compiled against, which is a red
  herring. See `StoryCmdletBase.InstallDependencyResolver`.

---

## How It Fits Together

- **`Taskfile.yml`** — the authoritative task graph. Simple verbs (restore,
  build, test, verify, clean) are defined directly here so the graph restores
  once, builds once, and tests without rebuilding.
- **`script/*.ps1`** — only genuinely complex, multi-step behavior (`image`,
  `publish`, `doctor`). Task invokes these in their own process.
- **`.env`** (committed) / **`.env.local`** (gitignored, wins) — per-developer
  environment preferences such as configuration. The authoritative build
  **target is in `Taskfile.yml`**, not in `.env`, so everyone verifies the same
  projects.

Do not reintroduce a general-purpose PowerShell orchestration layer; Task owns
orchestration.

---

## Architectural Principles (Stories-Specific)

- Stories are append-only.
- Events must be immutable.
- Public models are long-lived contracts.
- Serialization format changes require explicit approval.
- Determinism is critical.
- Avoid hidden side effects.
- Favor explicitness over cleverness.

---

## Testing Expectations

- All behavior changes must include or update tests.
- Tests must be deterministic.
- Never use randomness in tests.
- Prefer fixed GUIDs, timestamps, and constants.

---

## What NOT To Do

- Do not change event shapes without explicit instruction.
- Do not rename public APIs without confirmation.
- Do not introduce implicit behavior.
- Do not remove backward compatibility without discussion.

---

## When Unsure

Ask before:

- Changing schemas
- Modifying serialization
- Altering public contracts
- Introducing new architectural patterns

---

## Notes

The canonical/shared version of common PowerShell utilities lives in the
BigRedProf foundation repository:

```text
foundation/templates/dotnet/script/common.ps1
```

Each repository contains its own versioned copy under `script/common.ps1` so
repositories can evolve independently.
