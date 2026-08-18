<#
Tests for the BigRedProf.Stories PowerShell module.

Plain assertions rather than a framework, deliberately. Pester would be the
obvious choice, but the version shipped with Windows PowerShell is 3.x and CI
runs a different one, so a test written against Pester 5 syntax passes in one
place and fails in the other for reasons that have nothing to do with the module.
This needs no install, runs identically everywhere, and prints the same PASS
lines as the kiosk tests in the digihouse repository.

Everything here runs WITHOUT a stories service. The cmdlets validate their
arguments before they open a connection, so argument handling, the manifest, the
exported surface, and the format data are all testable offline; a test that needs
a live service belongs in an integration suite, not this one.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:passed = 0
$script:failed = 0

function Test-That
{
	param(
		[Parameter(Mandatory = $true)] [string] $Description,
		[Parameter(Mandatory = $true)] [scriptblock] $Condition
	)

	$ok = $false
	$detail = ''
	try
	{
		$ok = [bool] (& $Condition)
	}
	catch
	{
		$detail = $_.Exception.Message
	}

	if ($ok)
	{
		$script:passed++
		Write-Host ("  PASS  {0}" -f $Description)
	}
	else
	{
		$script:failed++
		Write-Host ("  FAIL  {0} {1}" -f $Description, $detail) -ForegroundColor Red
	}
}

$moduleDirectory = Join-Path $PSScriptRoot '..\..\artifacts\module\BigRedProf.Stories'
if (-not (Test-Path -LiteralPath $moduleDirectory))
{
	Write-Host "[test:powershell] Module not staged. Run 'task module' first." -ForegroundColor Red
	exit 1
}

$manifestPath = Join-Path $moduleDirectory 'BigRedProf.Stories.psd1'

Write-Host ''
Write-Host '[test:powershell] manifest'
Test-That 'the manifest is valid' { $null -ne (Test-ModuleManifest -Path $manifestPath) }
$manifest = Test-ModuleManifest -Path $manifestPath
Test-That 'the module is named BigRedProf.Stories, not the assembly name' { $manifest.Name -eq 'BigRedProf.Stories' }
Test-That 'it declares a license' { $null -ne $manifest.PrivateData.PSData.LicenseUri }
Test-That 'it requires PowerShell 7 or later' { $manifest.PowerShellVersion -ge [version]'7.0' }
Test-That 'it exports exactly the two cmdlets' {
	($manifest.ExportedCmdlets.Keys | Sort-Object) -join ',' -eq 'Get-Story,Watch-Story'
}
Test-That 'it ships the format data' { $manifest.ExportedFormatFiles.Count -eq 1 }

Write-Host ''
Write-Host '[test:powershell] import'
Import-Module $moduleDirectory -Force -ErrorAction Stop
Test-That 'the module imports' { $null -ne (Get-Module BigRedProf.Stories) }
Test-That 'Get-Story is available' { $null -ne (Get-Command Get-Story -ErrorAction SilentlyContinue) }
Test-That 'Watch-Story is available' { $null -ne (Get-Command Watch-Story -ErrorAction SilentlyContinue) }

Write-Host ''
Write-Host '[test:powershell] cmdlet surface'
$get = Get-Command Get-Story
$watch = Get-Command Watch-Story

Test-That 'Get-Story returns StoryThingInfo' {
	$get.OutputType.Name -contains 'BigRedProf.Stories.PowerShell.StoryThingInfo'
}
Test-That 'StoryId is mandatory and positional' {
	$p = $get.Parameters['StoryId'].Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] }
	$p.Mandatory -and $p.Position -eq 0
}
Test-That 'BaseUri is mandatory' {
	($get.Parameters['BaseUri'].Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] }).Mandatory
}
Test-That 'Get-Story has -First, which Watch-Story does not' {
	$get.Parameters.ContainsKey('First') -and -not $watch.Parameters.ContainsKey('First')
}
Test-That 'both share the reading parameters' {
	$shared = @('StoryId', 'BaseUri', 'Bookmark', 'ThingSchemaId', 'ModelAssembly')
	# @() because Where-Object returns a scalar for one match and $null for none,
	# and neither has a .Count under StrictMode.
	@($shared | Where-Object { -not ($get.Parameters.ContainsKey($_) -and $watch.Parameters.ContainsKey($_)) }).Count -eq 0
}
Test-That 'nothing in the surface mentions an envelope' {
	# The boundary from #5: envelopes are the caller's concept, not this module's.
	($get.Parameters.Keys + $watch.Parameters.Keys) -notmatch 'envelope'
}

Write-Host ''
Write-Host '[test:powershell] argument validation happens before any connection'
# NOTE: there is deliberately no "malformed story id" test. A TextTrail is
# permissive -- 'not a story id' parses perfectly well, spaces and all -- so the
# InvalidStoryId path is close to unreachable from text input. Asserting it would
# mean inventing a failure the type does not actually have.
Test-That 'an empty story id is rejected' {
	try
	{
		Get-Story '' -BaseUri 'http://localhost:1' -ErrorAction Stop | Out-Null
		$false
	}
	catch
	{
		$_.FullyQualifiedErrorId -like 'ParameterArgumentValidation*'
	}
}
Test-That 'a missing model assembly is reported as such' {
	try
	{
		Get-Story 'bigredprof/nope' -BaseUri 'http://localhost:1' -ModelAssembly 'C:\does\not\exist.dll' -ErrorAction Stop | Out-Null
		$false
	}
	catch
	{
		$_.FullyQualifiedErrorId -like 'ModelAssemblyNotFound*'
	}
}

Write-Host ''
if ($script:failed -gt 0)
{
	Write-Host ("[test:powershell] FAILED: {0} passed, {1} failed." -f $script:passed, $script:failed) -ForegroundColor Red
	exit 1
}

Write-Host ("[test:powershell] OK: {0} checks passed." -f $script:passed)
