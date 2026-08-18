param(
	[ValidateSet("Release", "Debug")]
	[string] $Configuration = "Release",

	# Publish to the PowerShell Gallery after staging. CI passes this on a
	# psmodule-v* tag; nobody should pass it by hand.
	[switch] $Publish,

	# The PowerShell Gallery API key. Only read when -Publish is given.
	[string] $ApiKey
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'common.ps1')

<#
Stage the BigRedProf.Stories PowerShell module, and optionally publish it.

WHY THIS IS NOT `dotnet pack`
-----------------------------
A PowerShell module package is not a library .nupkg. The manifest and assemblies
sit at the package ROOT, not under lib/<tfm>/, and the package carries a module
manifest rather than a nuspec's dependency graph. `dotnet pack` cannot produce
that shape, which is why BigRedProf.Stories.PowerShell.csproj sets
IsPackable=false and publishing goes through Publish-PSResource against a staged
directory instead.

THE NAME SPLIT IS DELIBERATE
----------------------------
The ASSEMBLY is BigRedProf.Stories.PowerShell; the MODULE is BigRedProf.Stories.
Module and assembly names are independent, and ".PowerShell" is redundant in the
name a person types at a PowerShell prompt (stories#5).

VERSION
-------
Comes from MinVer via the psmodule-v* tag. The build writes it to
module-version.txt beside the assembly and this script reads it back, so the
manifest cannot disagree with the binary it ships. An untagged build is a
prerelease, which the PowerShell Gallery accepts but only serves to callers who
ask for it.

This script also runs on Linux in CI, so every path here uses forward slashes:
Join-Path treats a backslash as an ordinary filename character there.
#>

$repoRoot = Get-RepoRoot
$moduleName = 'BigRedProf.Stories'
$projectDirectory = Join-Path $repoRoot 'src/PowerShell'
$buildOutput = Join-Path $projectDirectory "bin/$Configuration/net8.0"
$stagingRoot = Join-Path $repoRoot 'artifacts/module'
$stagingDirectory = Join-Path $stagingRoot $moduleName

Write-Step "building the module ($Configuration)"
Invoke-Checked "dotnet" @(
	"build",
	(Join-Path $projectDirectory 'BigRedProf.Stories.PowerShell.csproj'),
	"-c", $Configuration,
	"--nologo"
)

# Read the version MSBuild wrote beside the assembly rather than recomputing it.
# MinVer already resolved it during the build, and asking twice is how the
# manifest and the binary start to disagree. A text file rather than
# FileVersionInfo because this also runs on Linux in CI.
$assemblyPath = Join-Path $buildOutput 'BigRedProf.Stories.PowerShell.dll'
if (-not (Test-Path -LiteralPath $assemblyPath))
{
	throw "The module assembly was not found at $assemblyPath."
}

$versionPath = Join-Path $buildOutput 'module-version.txt'
if (-not (Test-Path -LiteralPath $versionPath))
{
	throw "The build did not write $versionPath. See the WriteModuleVersion target."
}

$informationalVersion = (Get-Content -LiteralPath $versionPath -Raw).Trim()

# Strip build metadata (+sha) and any prerelease suffix: a module manifest's
# ModuleVersion must be a plain n.n.n, and the prerelease part travels separately
# in PrivateData.PSData.Prerelease.
$version = $informationalVersion.Split('+')[0]
$prerelease = ''
if ($version.Contains('-'))
{
	$parts = $version.Split('-', 2)
	$version = $parts[0]
	# Gallery prerelease strings are alphanumerics only -- no dots, no hyphens.
	$prerelease = ($parts[1] -replace '[^A-Za-z0-9]', '')
}

Write-Step "staging $moduleName $informationalVersion"

if (Test-Path -LiteralPath $stagingDirectory)
{
	Remove-Item -Recurse -Force $stagingDirectory
}
New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null

# Everything the module needs at run time, flat at the module root. The pack rat
# assemblies travel with it: the module's dependency resolver probes this very
# directory (see StoryCmdletBase.InstallDependencyResolver), so a missing file
# here surfaces as an assembly load failure at first use, not at import.
Copy-Item -Path (Join-Path $buildOutput '*') -Destination $stagingDirectory -Recurse -Force

Copy-Item `
	-Path (Join-Path $projectDirectory 'BigRedProf.Stories.Format.ps1xml') `
	-Destination $stagingDirectory `
	-Force

$manifestPath = Join-Path $stagingDirectory "$moduleName.psd1"
$manifestArguments = @{
	Path                 = $manifestPath
	RootModule           = 'BigRedProf.Stories.PowerShell.dll'
	ModuleVersion        = $version
	GUID                 = 'ff2c0a2a-6d3b-4f3e-9f2a-7c0b5a1d8e64'
	Author               = 'BigRedProf'
	CompanyName          = 'Big Red Professor'
	Copyright            = 'Big Red Professor'
	Description          = 'Read and monitor BigRedProf stories from the command line.'
	PowerShellVersion    = '7.2'
	CompatiblePSEditions = @('Core')
	CmdletsToExport      = @('Get-Story', 'Watch-Story')
	FunctionsToExport    = @()
	VariablesToExport    = @()
	AliasesToExport      = @()
	FormatsToProcess     = @("$moduleName.Format.ps1xml")
	ProjectUri           = 'https://github.com/BigRedProf/stories'
	LicenseUri           = 'https://github.com/BigRedProf/stories/blob/main/LICENSE'
	Tags                 = @('BigRedProf', 'Stories', 'EventSourcing', 'Diagnostics')
}

if ($prerelease)
{
	$manifestArguments['Prerelease'] = $prerelease
}

New-ModuleManifest @manifestArguments

# Catches a malformed manifest here rather than at Publish-PSResource, which
# reports it far less clearly.
Test-ModuleManifest -Path $manifestPath | Out-Null

Write-Host ""
Write-Host "[module] OK: staged to $stagingDirectory"

if (-not $Publish)
{
	Write-Host "[module] Not publishing. Pass -Publish with -ApiKey to release."
	return
}

if ([string]::IsNullOrWhiteSpace($ApiKey))
{
	throw "-ApiKey is required when publishing. CI supplies it from the BIGREDPROF_PSG_API_KEY secret."
}

Write-Step "publishing $moduleName $version to the PowerShell Gallery"
Publish-PSResource -Path $stagingDirectory -ApiKey $ApiKey -Repository PSGallery

Write-Host ""
Write-Host "[module] OK: published $moduleName $informationalVersion."
