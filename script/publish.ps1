param(
	[Parameter(Mandatory = $true)]
	[string] $Tag
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot\common.ps1"

<#
Tag and push the BigRedProf.Stories.Api container image to the GitHub Container
Registry.

This publishes the image that is ALREADY BUILT locally -- it does not build it.
Run `task image` first.

NOTE this is the container image, NOT the BigRedProf.Stories.* NuGet packages.
Those are packed and pushed by .github/workflows/dotnet.yml on a push to main;
`task pack` only builds them locally. Two different artifacts, two different
destinations.

Digihouse also builds and publishes this image as part of its release, because a
digihouse deployment is a five-image unit at a single tag. Publishing from here
is for releasing stories on its own.
#>

$image = "bigredprofstoriesapi"

if ([string]::IsNullOrWhiteSpace($env:GITHUB_PAT_PACKAGE_REGISTRY))
{
	throw "GITHUB_PAT_PACKAGE_REGISTRY is not set. Publishing needs it to authenticate to ghcr.io."
}

# Fail before touching the registry if the image was never built, rather than
# discovering the gap mid-push.
& docker image inspect $image *> $null

if ($LASTEXITCODE -ne 0)
{
	throw "Local image '$image' not found. Run ``task image`` before publishing."
}

# Docker Desktop's cached ghcr.io session can go stale (e.g. after months of
# inactivity, or a Docker Desktop restart), causing "denied: denied" on push
# even though the PAT itself is fine. Log in explicitly so publishing doesn't
# silently depend on whatever session happens to already be cached.
#
# The PAT goes in over stdin rather than as a -p argument so it stays out of the
# process list and shell history.
Write-Step "docker login ghcr.io"
$env:GITHUB_PAT_PACKAGE_REGISTRY | & docker login ghcr.io -u bigredprof --password-stdin

if ($LASTEXITCODE -ne 0)
{
	throw "docker login failed with exit code ${LASTEXITCODE}."
}

$remote = "ghcr.io/bigredprof/${image}:${Tag}"

Write-Step "publishing $image -> $remote"
Invoke-Checked "docker" @("tag", $image, $remote)
Invoke-Checked "docker" @("push", $remote)

Write-Host ""
Write-Host "[publish] OK: published $image with tag '$Tag'."
