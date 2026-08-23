<#
.SYNOPSIS
	Packs the NuGet packages into the shared local feed, for testing without publishing.

.DESCRIPTION
	Testing a package change across the BigRedProf repositories used to mean publishing a
	prerelease to nuget.org and waiting for it to index -- minutes per attempt, and a version
	number spent forever whether or not the change was any good.

	This packs into a folder feed the sibling repositories restore from instead. It is not a
	shortcut around packaging: it produces the real .nupkg, in Release, so what a consumer
	restores is what CI would have published. That matters here more than usual, because
	a real .nupkg is what a consumer restores, so packaging mistakes surface here rather
	than after publishing.

	Every pack gets a unique version. NuGet's global packages cache is keyed on id and version,
	so re-packing the same version silently changes nothing: the consumer keeps restoring the
	copy it already unpacked. A timestamp sidesteps that entirely rather than asking everyone to
	remember to clear caches.

.PARAMETER Version
	The version to pack. Defaults to <MinVerMinimumMajorMinor>.0-local.<timestamp>, so it tracks
	the release the repository is working toward.

.PARAMETER Feed
	The folder feed. Defaults to $env:BRP_LOCAL_FEED, then to .local-feed beside the repositories.
#>

[CmdletBinding()]
param(
	[string] $Version,

	[string] $Feed
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

$repoRoot = Get-RepoRoot
Push-Location $repoRoot
try
{
	# ---- where ----------------------------------------------------------------------------
	if ([string]::IsNullOrWhiteSpace($Feed))
	{
		$Feed = $env:BRP_LOCAL_FEED
	}
	if ([string]::IsNullOrWhiteSpace($Feed))
	{
		# Beside the repositories rather than inside one: every repository packs into the same
		# feed, and consumers read from it.
		$Feed = Join-Path (Split-Path -Parent $repoRoot) '.local-feed'
	}

	if (-not (Test-Path $Feed))
	{
		New-Item -ItemType Directory -Path $Feed -Force | Out-Null
	}
	$Feed = (Resolve-Path $Feed).Path

	# ---- what version ---------------------------------------------------------------------
	if ([string]::IsNullOrWhiteSpace($Version))
	{
		# Track whatever release this repository is working toward, so a local package reads
		# as a prerelease of that rather than of nothing.
		$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
		$match = [regex]::Match($props, '<MinVerMinimumMajorMinor>([0-9]+\.[0-9]+)</MinVerMinimumMajorMinor>')
		$majorMinor = if ($match.Success) { $match.Groups[1].Value } else { '0.0' }

		$stamp = (Get-Date).ToString('yyyyMMddHHmmss')
		$Version = "$majorMinor.0-local.$stamp"
	}

	# ---- pack -----------------------------------------------------------------------------
	# Release, because that is what a consumer of a published package would get. A Debug
	# package can differ in ways that only show up once it is too late to matter.
	Write-Step "Packing $Version into $Feed"
	Invoke-Checked -Command 'dotnet' -Arguments @(
		'pack', 'src/stories.sln',
		'-c', 'Release',
		'-o', $Feed,
		'--nologo',
		"-p:MinVerVersionOverride=$Version"
	)

	# ---- keep the feed from growing forever ------------------------------------------------
	# Only local packages are pruned; anything else in the folder is left alone.
	$keep = 5
	$groups = Get-ChildItem -Path $Feed -Filter '*-local.*.nupkg' |
		Group-Object { ($_.Name -split '\.[0-9]+\.[0-9]+\.[0-9]+-local\.')[0] }
	foreach ($group in $groups)
	{
		$stale = $group.Group | Sort-Object LastWriteTime -Descending | Select-Object -Skip $keep
		foreach ($file in $stale)
		{
			Remove-Item $file.FullName -Force
		}
	}

	Write-Host ""
	Write-Host "[pack:local] OK: packed $Version"
	Write-Host "[pack:local]   feed: $Feed"
	Write-Host "[pack:local] Point a consumer at it with:  task use:local -- $Version"
}
catch
{
	Write-Host ""
	Write-Host "[pack:local] FAILED: $($_.Exception.Message)" -ForegroundColor Red
	exit 1
}
finally
{
	Pop-Location
}
