# BigRedProf Repository Conventions

## Purpose

This document defines the standard repository structure, build conventions, and development environment philosophy for BigRedProf projects.

The primary goals are:

* Consistency across projects
* Predictable behavior for humans and AI agents
* Minimal environment-specific glue
* Reproducible builds
* Clear separation between source and generated artifacts
* Clean repository navigation
* Long-term maintainability

---

# Core Philosophy

## Repositories Should Teach Developers How To Build Them

Each repository should contain the canonical scripts and conventions required to:

* enter the development environment
* restore dependencies
* build
* test
* verify
* clean

External tools such as:

* Codex
* Claude
* Cursor
* GitHub Actions
* Azure Pipelines
* VS Code Tasks
* Rider
* Visual Studio

should act only as thin wrappers around the repository's own scripts.

The repository itself is the source of truth.

---

# Standard Repository Structure

Preferred repository layout:

```text
repo/
  src/
  tests/
  docs/
  scripts/
  artifacts/
  .codex/

  README.md
  global.json
```

## Directory Purposes

| Directory    | Purpose                                       |
| ------------ | --------------------------------------------- |
| `src/`       | Human-authored production source code         |
| `tests/`     | Human-authored validation and test code       |
| `docs/`      | Documentation                                 |
| `scripts/`   | Repository automation scripts                 |
| `artifacts/` | Machine-generated outputs                     |
| `.codex/`    | Minimal Codex-specific wrappers/configuration |

---

# Source Code Organization

## Short Directory Names

Project directories should use short contextual names.

Example:

```text
src/
  Core/
  Mobile/
  Web/
```

while assemblies and namespaces remain globally descriptive:

```text
BigRedProf.Solora.Core
BigRedProf.Solora.Mobile
BigRedProf.Solora.Web
```

This convention intentionally separates:

| Concern                    | Optimization                               |
| -------------------------- | ------------------------------------------ |
| Namespace / assembly names | Global uniqueness and clarity              |
| Directory names            | Human navigation and filesystem ergonomics |

Benefits:

* shorter filesystem paths
* cleaner terminal usage
* easier navigation
* reduced path-length problems on Windows
* clearer repo layouts

The mapping between directory names and assembly names should remain highly regular and predictable.

Example:

```text
src/Core
  -> BigRedProf.Solora.Core

src/Mobile
  -> BigRedProf.Solora.Mobile
```

Irregular mappings should be avoided.

---

# Tests Organization

Tests should generally live under a top-level `tests/` directory.

Example:

```text
tests/
  Core.Tests/
  Mobile.Tests/
```

This separates:

| Directory | Meaning                             |
| --------- | ----------------------------------- |
| `src/`    | Things we ship                      |
| `tests/`  | Things that validate shipped things |

Benefits:

* clearer dependency boundaries
* cleaner repo navigation
* simpler CI orchestration
* easier AI agent reasoning
* improved long-term scalability

---

# Task Runner

Repositories use [Task](https://taskfile.dev) (`go-task`) as their command entry
point **and** their orchestration layer. Repositories do not maintain a
general-purpose build framework of their own. The design intent:

* one predictable verb set across every repository (`task build`, `task test`,
  `task verify`, `task clean`, `task doctor`, ...)
* runs from a fresh shell with no initialization — humans and AI agents use the
  identical interface
* the environment is loaded automatically on every invocation, so nothing needs
  to be sourced first
* the task graph avoids duplicated work: restore once, build once, test without
  rebuilding (`--no-build --no-restore`)
* slow, on-demand work (container images, Unity player builds, integration
  tests, release outputs) lives in separate tasks kept out of the fast
  `build`/`verify` inner loop
* for monorepos, each project carries its own `Taskfile.yml` (with its own
  authoritative target) pulled into the root via `includes:`, so `task build`
  at the root builds everything while `task build` inside a project builds only
  that project

## Simple verbs live in `Taskfile.yml`; complex behavior lives in scripts

The simple verbs are defined directly in `Taskfile.yml` as the authoritative
task graph. `script/*.ps1` exist only for genuinely complex, multi-step behavior
that would be awkward in YAML. Task invokes those scripts in their own process.

| Task                   | Where it lives          | Purpose                                    |
| ---------------------- | ----------------------- | ------------------------------------------ |
| `restore`              | Taskfile                | Restore dependencies for the target        |
| `build`                | Taskfile                | Compile the target (fast)                  |
| `test`                 | Taskfile                | Unit tests, no rebuild                     |
| `verify`               | Taskfile                | Everything required before merging         |
| `clean`                | Taskfile                | Remove build outputs and artifacts         |
| `image`                | `script/image.ps1`      | Build the container image (SLOW)           |
| `test:integration`     | `script/integration-test.ps1` | Image + integration tests            |
| `player`               | `script/unity-player.ps1` | Unity player build (SLOW)                |
| `doctor`               | `script/doctor.ps1`     | Toolchain/version diagnostics              |

---

# Environment

Repository environment variables live in layered `.env` files, not in shell
scripts:

| File               | Purpose                                             |
| ------------------ | --------------------------------------------------- |
| `.env`             | Committed base environment (per-developer defaults) |
| `.env.local`       | Gitignored per-developer overrides; wins over `.env`|

Task loads these on every invocation (`dotenv: ['.env.local', '.env']`), so the
environment is present without assuming the agent harness or shell set anything.
A missing `.env.local` is not an error. Files are standard dotenv (`KEY=VALUE`),
parsed by Task.

The **authoritative build target is defined in `Taskfile.yml`**, not in `.env`,
so two developers cannot run `task verify` against different projects. `.env`
holds only genuine per-developer preferences (e.g. configuration).

---

# verify — the canonical success criterion

`verify` is the single command that means "everything required before merging."
It is fast by design (build + unit tests) so it can run as the inner-loop
success criterion for humans and agents. It should NOT include slow container or
Unity builds.

Related, explicitly separate tasks:

| Task               | Meaning                                            |
| ------------------ | -------------------------------------------------- |
| `build`            | compile                                            |
| `test`             | unit tests                                         |
| `image`            | container image                                    |
| `test:integration` | image + integration tests                          |
| `player`           | Unity player                                       |
| `verify`           | everything required before merging                 |
| `package`/`release`| release-only outputs                               |

If meaningful integration testing genuinely requires the API container image,
`verify` may include `test:integration` — but a Unity player is typically
release-only and stays out of `verify`.

## Do not fake up-to-date checks for slow builds

Do NOT guard container or Unity builds with sentinel/`generates:` files. A
sentinel can exist while the image was deleted, a base image moved, or an
undeclared input (`Directory.Build.props`, `.dockerignore`, `NuGet.Config`)
changed. Always invoke the real build (`docker build`) and let the tool's own
cache (BuildKit) decide what to rebuild. Use `sources:`/`generates:` only when
the generated files genuinely represent the result.

---

# Test targets should be explicit

Test the solution (or explicitly defined test targets), not projects discovered
by name matching. Name-based discovery silently passes when a repository is
misconfigured ("no test projects found") — a dangerous failure mode. A
repository that intentionally has no tests should opt out explicitly.

---

# Artifacts

## Philosophy

Generated outputs should be separated from human-authored source code whenever practical.

Preferred structure:

```text
artifacts/
  bin/
  obj/
```

Additional directories may include:

```text
artifacts/
  packages/
  logs/
  coverage/
  test-results/
  publish/
```

Benefits:

* cleaner source directories
* simpler repository navigation
* easier cleanup
* simpler AI reasoning
* clearer distinction between authored and generated content

---

# Compatibility With Microsoft Tooling

Some Microsoft SDKs and workloads may assume project-local `bin/` and `obj/` directories.

Examples may include:

* MAUI
* iOS/Android workloads
* generated XAML systems
* Unity-related tooling
* specialized SDK targets

Therefore:

> Root-level artifacts are preferred when practical.
>
> Project-local `bin/obj` directories are acceptable compatibility leaks when required by tooling.

Projects should not aggressively fight SDK assumptions if doing so creates fragility.

---

# clean

The `clean` task should aggressively remove generated artifacts — typically
`dotnet clean` plus removing the `artifacts/` tree, and optionally project-local
`bin/`, `obj/`, `__pycache__/`, or other temporary outputs.

The philosophy:

> Generated artifacts should be disposable.

---

# Monorepo Philosophy

The default preference is monorepo organization while projects:

* share concepts
* share release cadence
* share ownership
* evolve together
* frequently refactor together

Example:

```text
src/
  Core/
  Mobile/
  Web/
  Python.Core/
```

Repositories should typically be split only when:

* release cadence diverges significantly
* ownership diverges
* CI complexity becomes painful
* ecosystem constraints strongly differ
* projects become independently reusable

Guiding principle:

> If projects change together, they should usually live together.

---

# Codex Integration

Codex configuration should remain intentionally minimal.

Example:

```toml
[setup.win32]
script = "pwsh -NoProfile -ExecutionPolicy Bypass -File ./.codex/setup.ps1"
```

Codex should reuse repository-native tasks and scripts rather than defining
separate behavior. Once set up, agents run `task build`, `task verify`, etc.,
the same as humans.

The `.codex/` directory should contain only:

* thin wrappers
* environment configuration
* minimal agent-specific glue

The repository scripts remain the source of truth.

---

# Guiding Principles Summary

1. Repositories are self-describing.
2. Task is the orchestration layer; simple verbs live in the Taskfile, scripts hold only complex behavior.
3. Human-authored and generated content should be clearly separated.
4. AI agents should operate through the same interfaces as humans.
5. Naming should optimize for clarity and consistency.
6. Prefer simple and predictable conventions.
7. Avoid fighting tooling ecosystems when the cost exceeds the benefit.
8. Monorepos are preferred while systems evolve together.
9. Thin wrappers are preferred over duplicated orchestration logic.
10. Generated artifacts should be disposable.
11. Environment lives in layered `.env` files, loaded automatically — never assumed to be pre-set.
12. Keep painfully slow work (images, Unity) out of the fast inner loop as explicit tasks; never fake caching with sentinel files — let the real tool decide.
