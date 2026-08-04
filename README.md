# stories
Stories capture a series of things over time.

## Development

This repository is driven by [Task](https://taskfile.dev). Install it once per
machine:

```powershell
choco install go-task
```

Then, from the repository root:

```powershell
task --list      # see available tasks
task verify      # build + unit tests — everything required before merging
task build       # fast inner loop
task doctor      # toolchain/version diagnostics
```

Task loads the layered environment (`.env.local` then `.env`) on every
invocation, so no shell setup is required — commands work in a fresh process for
humans and agents alike. Put private overrides (such as
`GITHUB_PAT_PACKAGE_REGISTRY`) in a gitignored `.env.local`.

Note the solution lives at `src/stories.sln`, not at the repository root.

### Slow, on-demand tasks

These are deliberately excluded from the fast `build`/`verify` inner loop:

```powershell
task image              # build the Api container image (SLOW)
task publish -- <tag>   # tag and push the Api image to ghcr.io
task pack               # build the NuGet packages locally
```

### Two different artifacts

| Artifact                              | Built by              | Published by                      |
| ------------------------------------- | --------------------- | --------------------------------- |
| `BigRedProf.Stories.*` NuGet packages | `task pack` (locally) | CI, on a push to `main`           |
| `bigredprofstoriesapi` image          | `task image`          | `task publish -- <tag>` (ghcr.io) |

`task pack` deliberately cannot push — package publishing stays in
`.github/workflows/dotnet.yml`, so nothing local can release a package by
accident. That workflow calls `task verify` for its build-and-test half, so CI
and local agree on what "it builds" means.

Digihouse also builds and publishes the Api image as part of its own release,
because a digihouse deployment is a five-image unit at a single tag.

## How it fits together

- **`Taskfile.yml`** — the authoritative task graph. Simple verbs (`restore`,
  `build`, `test`, `verify`, `clean`) live here so the graph restores once,
  builds once, and tests without rebuilding.
- **`script/*.ps1`** — only genuinely complex behavior (`image`, `publish`,
  `doctor`). See [script/README.md](script/README.md).
- **`.env`** — committed base environment. **`.env.local`** — gitignored
  per-developer overrides that win over `.env`. The build **target** is
  authoritative in `Taskfile.yml`, not `.env`.
- **`AGENTS.md`** — shared agent/contributor instructions; **`CLAUDE.md`**
  imports it. **`CODING_GUIDELINES.md`** is the authoritative code style.
