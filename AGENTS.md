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
task test       # unit tests, no rebuild
task verify     # everything required before merging — the success criterion
task clean
task doctor     # toolchain/version diagnostics
```

List everything with `task --list`.

`verify` is the canonical success criterion. It is fast by design (build +
unit tests). Slow, on-demand work is separate and intentionally excluded from
the inner loop:

```powershell
task image             # build the Api container image (SLOW)
task publish -- <tag>  # tag and push the Api image to ghcr.io
task pack              # produce the NuGet packages locally
```

Stories specifics:

- The build **target** is `src/stories.sln`. Note the solution lives under
  `src/`, not at the repository root.
- Unit tests are real here (unlike some sibling repos): `task test` runs
  `src/StoriesCli.Test`. It is still under `src/` rather than a top-level
  `tests/` directory, which is a known deviation from `REPO_CONVENTIONS.md`.
- `task image` and `task publish` need `GITHUB_PAT_PACKAGE_REGISTRY` in the
  environment (or `.env.local`). It is a secret and never goes in `.env`. The
  Dockerfile consumes it as a **BuildKit secret**, not a build arg, so it never
  appears in build output.
- This repository publishes the `BigRedProf.Stories.*` NuGet packages that
  digihouse and others consume. CI does that on a push to `main`; `task pack`
  only builds them locally.

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
