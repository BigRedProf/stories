<#
===============================================================================
BIGREDPROF COMMON POWERSHELL UTILITIES
===============================================================================

Canonical/source version:

	BigRedProf/foundation/templates/dotnet/script/common.ps1

Each repository contains its OWN COPY under:

	script/common.ps1

This is intentional. Build/developer tooling should be versioned with each
repository and updated deliberately.

These helpers are shared by the genuinely complex scripts (image, publish,
doctor). Simple verbs (restore/build/test/verify/clean) live directly in
Taskfile.yml — Task is the orchestration layer.

Keep this file small, boring, deterministic, and easy for humans and AI agents
to understand.
===============================================================================
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step
{
	param(
		[Parameter(Mandatory = $true)]
		[string] $Message
	)

	Write-Host ""
	Write-Host "[step] $Message"
}

function Invoke-Checked
{
	param(
		[Parameter(Mandatory = $true)]
		[string] $Command,

		[Parameter(Mandatory = $true)]
		[string[]] $Arguments
	)

	& $Command @Arguments

	if ($LASTEXITCODE -ne 0)
	{
		throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
	}
}

function Test-CommandExists
{
	param(
		[Parameter(Mandatory = $true)]
		[string] $Name
	)

	return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

<#
Repository root, resolved from this script's own location rather than the
current directory. Task always invokes scripts from the repository root, but
resolving explicitly keeps the scripts correct no matter who calls them — and
lets the Docker builds use real paths instead of hard-coded absolute ones.
#>
function Get-RepoRoot
{
	return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

<#
Check that the .env files are UTF-8, returning $true when they are usable.

Task's dotenv parser reads UTF-8 only. A UTF-16 file -- which is exactly what
Windows PowerShell 5.1's `>` and `>>` redirection produces -- fails to parse,
AND Task's error message ECHOES THE FILE'S CONTENTS. A .env.local holding a PAT
therefore leaks it to the console and into any captured log.

IMPORTANT: Task reads the dotenv files at startup, BEFORE it invokes any task,
so `task doctor` can never reach this check -- Task has already failed and
printed the file. This is why the check lives in common.ps1 and is called by
callers that run OUTSIDE Task (.codex/setup.ps1, and doctor.ps1 when it is
invoked directly as `pwsh -File script/doctor.ps1`). That direct invocation is
the escape hatch for exactly the situation where Task itself will not run.
#>
function Test-DotEnvEncoding
{
	param(
		[string] $RepoRoot = (Get-RepoRoot)
	)

	$ok = $true

	foreach ($name in @(".env", ".env.local"))
	{
		$path = Join-Path $RepoRoot $name

		if (-not (Test-Path -LiteralPath $path -PathType Leaf))
		{
			continue
		}

		$bytes = [System.IO.File]::ReadAllBytes($path)

		$isUtf16 = $bytes.Length -ge 2 -and (
			($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or
			($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF)
		)

		if ($isUtf16)
		{
			Write-Host ""
			Write-Host " $name : UTF-16 — Task cannot parse this, and its parse error will ECHO THE FILE'S CONTENTS."
			Write-Host "   -> Rewrite as UTF-8 (pwsh 7):"
			Write-Host "      `$t = [IO.File]::ReadAllText('$name', [Text.Encoding]::Unicode); [IO.File]::WriteAllText('$name', `$t, (New-Object Text.UTF8Encoding `$false))"
			Write-Host "   -> If it held a secret, treat that secret as EXPOSED and rotate it."
			$ok = $false
		}
		elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
		{
			# A UTF-8 BOM parses, but the BOM becomes part of the first key's name.
			Write-Host ""
			Write-Host " $name : UTF-8 with BOM — the BOM corrupts the FIRST key's name. Rewrite without a BOM."
			$ok = $false
		}
	}

	return $ok
}

Write-Host "[common] BigRedProf common utilities loaded."
