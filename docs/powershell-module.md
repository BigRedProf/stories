# Reading stories from PowerShell

The `BigRedProf.Stories` module reads and monitors stories from the command line
and returns **objects**, so filtering and shaping are a pipeline rather than a
grep.

```powershell
Install-PSResource BigRedProf.Stories
```

Two cmdlets:

| Cmdlet | What it does |
| --- | --- |
| `Get-Story` | Reads a range and **returns**. Use it to ask a question and get an answer. |
| `Watch-Story` | Tails, emitting each thing as it lands, until you stop it. |

```powershell
Get-Story bigredprof/digihouse/catalog -BaseUri http://localhost:43027
```

Shared parameters: `-StoryId` (positional), `-BaseUri`, `-Bookmark`,
`-ThingSchemaId`, `-ModelAssembly`. `Get-Story` adds `-First`.

## Why objects

The three questions worth asking of a story are all filters, and all of them are
awkward against text:

```powershell
# by event type
$things | Where-Object { $_.Model.Payload.Model.GetType().Name -eq 'GoodMoved' }

# by actor
$things | Where-Object { $_.Model.AuthorId -eq $agentId }

# by range -- and this one is server-side, not a filter at all
Get-Story $story -BaseUri $uri -Bookmark 200 -First 50
```

Each thing comes back as a `StoryThingInfo` carrying `Offset`, the raw `Thing`
code, and the decoded `Model`. The offset travels with the thing deliberately: it
is the bookmark the story replays from, so it is the coordinate to quote when
reporting what you saw.

## Decoding, and the boundary

Stories stores opinion-free codes and knows nothing about what they mean. So the
module decodes nothing by default — omit `-ThingSchemaId` and you get raw codes,
which is the honest answer, because only the caller knows what its own story
contains.

To decode, name the schema every thing is packed as and supply the assemblies
carrying the pack rats:

```powershell
Get-Story bigredprof/digihouse/catalog -BaseUri http://localhost:43027 `
    -ThingSchemaId 3d56d126-3766-468e-9818-389ead598da3 `
    -ModelAssembly C:\path\to\BigRedProf.Digihouse.Models.dll
```

That GUID is digihouse's `Envelope`, not something this module knows about.
Apps that wrap every thing in an envelope of their own record things this way;
the wrapper, the short story names, and resolving an author id to a person all
belong to the app, not here. **If a parameter is ever named `-Envelope`, we got
it wrong.**

A thing whose schema this host does not know is not an error — a story outlives
the build that wrote it. Those come back with `Model` null, a warning naming the
offset, and the raw code intact.

## The load-context trap

Worth knowing before it costs you an afternoon.

PowerShell loads a binary module into the **default** assembly load context, and
that context probes **pwsh's** directory, not the module's. So an assembly passed
to `-ModelAssembly` cannot find its own dependencies, and the failure surfaces as
a `ReflectionTypeLoadException` during pack rat registration, naming the exact
**version** of `BigRedProf.Data.Core` the model assembly was compiled against.

That version is a red herring. Loading by path ignores version, so a newer
`Data.Core` satisfies an assembly built against an older one — which is the
normal case. The module installs a `Resolving` handler over its own directory and
each model assembly's directory to fix this
(`StoryCmdletBase.InstallDependencyResolver`). If you ever see that error anyway,
the missing file is next to the model assembly, not a version mismatch.

## Releasing

The module has its own version line, independent of the four
`BigRedProf.Stories.*` NuGet packages, because it is packaged separately and by a
different mechanism. Two tag prefixes keep them apart:

| Tag | Releases | To |
| --- | --- | --- |
| `v0.9.0` | the four library packages | nuget.org |
| `psmodule-v0.2.0` | the PowerShell module | PowerShell Gallery |

`task module` stages into `artifacts/module` and never publishes; releasing is
CI's job on a tag. Note the Gallery has no trusted-publishing equivalent, so
unlike the NuGet packages this path still uses a long-lived API key
(`BIGREDPROF_PSG_API_KEY`), scoped to this package and set to expire.

Packaging goes through `Publish-PSResource` rather than `dotnet pack`, because a
module package puts its manifest and assemblies at the package root — a shape
`dotnet pack` cannot produce. That is why the project sets `IsPackable=false`.
