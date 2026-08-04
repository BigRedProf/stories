$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. "$PSScriptRoot\common.ps1"

# Minimum Task (go-task) version this repository expects. Bump deliberately.
$requiredTaskVersion = [version]"3.38.0"

Write-Step "BigRedProf toolchain diagnostics"

$ok = $true

Write-Host ""
Write-Host "==============================================================="
Write-Host "                 BIGREDPROF DEVELOPMENT ENVIRONMENT"
Write-Host "==============================================================="
Write-Host (" Repository  : stories")
Write-Host (" Machine     : " + $env:COMPUTERNAME)
Write-Host (" PowerShell  : " + $PSVersionTable.PSVersion + " (" + $PSVersionTable.PSEdition + ")")
Write-Host (" OS          : " + [System.Environment]::OSVersion.VersionString)

# --- .NET SDK ---------------------------------------------------------------
if (Test-CommandExists "dotnet")
{
	$dotnetVersion = (& dotnet --version).Trim()
	Write-Host (" .NET SDK    : " + $dotnetVersion)
}
else
{
	Write-Host " .NET SDK    : <missing>"
	$ok = $false
}

# --- Task -------------------------------------------------------------------
if (Test-CommandExists "task")
{
	$taskVersionRaw = (& task --version).Trim()
	Write-Host (" Task        : " + $taskVersionRaw + " (required >= $requiredTaskVersion)")

	$match = [regex]::Match($taskVersionRaw, '\d+\.\d+\.\d+')
	if ($match.Success -and ([version]$match.Value) -lt $requiredTaskVersion)
	{
		Write-Host "   -> Task is older than the required version. Upgrade with: choco upgrade go-task"
		$ok = $false
	}
}
else
{
	Write-Host " Task        : <missing> — install with: choco install go-task"
	$ok = $false
}

# --- Docker -----------------------------------------------------------------
# Only needed for the on-demand image/publish tasks, never for build/verify, so
# a missing Docker is reported but not treated as a failure.
if (Test-CommandExists "docker")
{
	$dockerVersion = (& docker --version).Trim()
	Write-Host (" Docker      : " + $dockerVersion)
}
else
{
	Write-Host " Docker      : <missing> — only needed for: task image, task publish"
}

Write-Host "==============================================================="

# --- .env encoding ----------------------------------------------------------
# NOTE: when doctor runs via `task doctor` this always passes, because Task
# parses the dotenv files at startup and would already have failed. It earns its
# keep when doctor is invoked DIRECTLY:
#
#   pwsh -NoProfile -ExecutionPolicy Bypass -File script/doctor.ps1
#
# which is the way to diagnose a repository whose .env files stop Task running
# at all. See Test-DotEnvEncoding in common.ps1.
if (-not (Test-DotEnvEncoding))
{
	$ok = $false
}

Write-Host ""

if (-not $ok)
{
	throw "Toolchain diagnostics failed. Resolve the items marked above."
}

Write-Host "[doctor] OK: toolchain looks healthy."
