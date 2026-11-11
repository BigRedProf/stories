# Library semantics lab

Executable investigation for Stories #25, #26, and #27. The projects under `src/` are a prototype. They are not part of `src/stories.sln`, `task verify`, or any shipped package.

Read `REQUIREMENTS.md` for what exists versus what was only proposed, `DECISION.md` for the recommendation once it is written, `DECISIONS_NEEDED.md` for Russell's open calls, and `CHECKPOINT.md` for the resume point.

## Run

From this directory:

```bash
dotnet test src/SemanticsLab.Tests/BigRedProf.Stories.LibrarySemanticsLab.Tests.csproj --nologo
```

The randomized invariant test uses the fixed seed `20261110` and prints that seed. No live service, tape device, or package publish is required.

—CJ
