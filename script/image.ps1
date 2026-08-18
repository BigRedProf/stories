$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot\common.ps1"

<#
Build the BigRedProf.Stories.Api container image.

Deliberately NO sentinel/up-to-date file: a sentinel can exist while the image
was deleted, the base image moved, or an undeclared input (Dockerfile,
.dockerignore, NuGet.Config, ...) changed. Always invoke `docker build` and let
BuildKit's layer cache decide what actually needs rebuilding.

The image is NOT pre-built with `dotnet build` first. The Dockerfile restores
and builds inside the container, so a local build proves nothing about the
image. `task build` covers "does it compile".
#>

$repoRoot = Get-RepoRoot

# The Dockerfile copies NuGet.Config, .config/ and src/ from the repository
# root, so that is the build context.
$context = $repoRoot

if (-not (Test-CommandExists "docker"))
{
	throw "docker was not found on PATH."
}

Write-Step "docker build bigredprofstoriesapi"
Invoke-Checked "docker" @(
	"build",
	"--force-rm",
	"-t", "bigredprofstoriesapi",
	"-f", (Join-Path $repoRoot "src\Api\Dockerfile"),
	$context
)

Write-Host ""
Write-Host "[image] OK: built bigredprofstoriesapi."
