<#
.SYNOPSIS
	Points this repository at locally packed BigRedProf packages, or back at released ones.

.DESCRIPTION
	`task use:local -- <version>` switches every BigRedProf.* reference to a version packed into
	the shared local feed by a sibling repository's `task pack:local`, and adds that feed as a
	package source. `task use:local -- released` puts everything back.

	Which references get rewritten is decided by what is actually in the feed at that version,
	so packing only BigRedProf.Data leaves the Stories and Content references alone. Guessing
	from a name prefix would quietly retarget packages nobody rebuilt.

	The tool manifest is rewritten too. It is the easiest thing to forget, and forgetting it
	means the pack rat compiler keeps running the released build against locally built
	libraries -- which is exactly the mismatch this is meant to rule out.

	Both the package versions and NuGet.Config are tracked files, so switching to local shows
	up in `git status`. That is deliberate: it is hard to forget you are on local packages, and
	`task release` refuses to tag a dirty tree, so a local reference cannot reach a published
	version.

.PARAMETER Version
	A version in the local feed, or `released` to restore what was there before.

.PARAMETER Feed
	The folder feed. Defaults to $env:BRP_LOCAL_FEED, then to .local-feed beside the repositories.
#>

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true, Position = 0)]
	[string] $Version,

	[string] $Feed
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

$repoRoot = Get-RepoRoot
Push-Location $repoRoot
try
{
	$stateFile = Join-Path $repoRoot '.local-packages.json'
	$sourceName = 'bigredprof-local'

	# Read and write without disturbing the encoding. Set-Content drops a UTF-8 BOM that
	# Get-Content silently swallowed, so a file this only meant to change one version in comes
	# back re-encoded -- a diff nobody asked for, in a file nobody was looking at.
	function Read-TextFile
	{
		param([string] $Path)

		$bytes = [System.IO.File]::ReadAllBytes($Path)
		$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
		$text = [System.Text.Encoding]::UTF8.GetString($bytes)
		if ($hasBom)
		{
			$text = $text.Substring(1)
		}

		return [pscustomobject]@{ Text = $text; HasBom = $hasBom }
	}

	function Write-TextFile
	{
		param([string] $Path, [string] $Text, [bool] $HasBom)

		$encoding = New-Object System.Text.UTF8Encoding($HasBom)
		[System.IO.File]::WriteAllText($Path, $Text, $encoding)
	}

	function Get-ReferenceFiles
	{
		$files = @()
		$files += Get-ChildItem -Path $repoRoot -Filter '*.csproj' -Recurse |
			Where-Object { $_.FullName -notmatch '\\(obj|bin|Library)\\' } |
			ForEach-Object { $_.FullName }

		# Under central package management the csprojs carry bare references and every
		# version lives here, so this file is the only thing controlling which build of a
		# BigRedProf package gets restored. Miss it and the switcher retargets the tool
		# manifest while leaving the libraries behind -- which is precisely the
		# compiler-against-a-different-library mismatch this whole workflow exists to
		# prevent, arrived at by the tool meant to prevent it.
		$centralVersions = Join-Path $repoRoot 'Directory.Packages.props'
		if (Test-Path $centralVersions)
		{
			$files += $centralVersions
		}

		$manifest = Join-Path $repoRoot '.config/dotnet-tools.json'
		if (Test-Path $manifest)
		{
			$files += $manifest
		}

		return $files
	}

	# ---------------------------------------------------------------------------------------
	if ($Version -eq 'released')
	{
		if (-not (Test-Path $stateFile))
		{
			throw "No record of what was here before, so there is nothing to restore. Was task use:local ever run?"
		}

		Write-Step "Restoring released package versions"
		$state = Get-Content $stateFile -Raw | ConvertFrom-Json

		foreach ($entry in $state.references)
		{
			$file = Read-TextFile -Path $entry.file
			$text = $file.Text.Replace($entry.after, $entry.before)
			Write-TextFile -Path $entry.file -Text $text -HasBom $file.HasBom
			Write-Host "  $($entry.package): $($entry.beforeVersion)  <- $($entry.afterVersion)"
		}

		# Take the source back out, so a restore cannot silently prefer a local package.
		$nugetConfig = Join-Path $repoRoot 'NuGet.Config'
		if (Test-Path $nugetConfig)
		{
			$configFile = Read-TextFile -Path $nugetConfig
			$config = [regex]::Replace($configFile.Text, '\s*<add key="' + $sourceName + '"[^>]*/>', '')
			Write-TextFile -Path $nugetConfig -Text $config -HasBom $configFile.HasBom
		}

		Remove-Item $stateFile -Force
		Write-Host ""
		Write-Host "[use:packages] OK: back on released packages."
		Write-Host "[use:packages] Run ``dotnet tool restore`` before building."
		return
	}

	# ---------------------------------------------------------------------------------------
	if ([string]::IsNullOrWhiteSpace($Feed))
	{
		$Feed = $env:BRP_LOCAL_FEED
	}
	if ([string]::IsNullOrWhiteSpace($Feed))
	{
		$Feed = Join-Path (Split-Path -Parent $repoRoot) '.local-feed'
	}
	if (-not (Test-Path $Feed))
	{
		throw "There is no local feed at $Feed. Run task pack:local in the repository that produces the packages."
	}
	$Feed = (Resolve-Path $Feed).Path

	# What did that pack actually produce? Only those packages get retargeted.
	$packages = @(
		Get-ChildItem -Path $Feed -Filter "*.$Version.nupkg" |
			ForEach-Object { $_.Name.Substring(0, $_.Name.Length - ".$Version.nupkg".Length) }
	)
	if ($packages.Count -eq 0)
	{
		throw "The feed has nothing at version $Version. Run task pack:local, which prints the version it packed."
	}

	Write-Step "Switching to local packages at $Version"
	Write-Host "  feed: $Feed"
	foreach ($package in $packages)
	{
		Write-Host "  found: $package"
	}

	$references = @()
	foreach ($file in Get-ReferenceFiles)
	{
		$textFile = Read-TextFile -Path $file
		$content = $textFile.Text
		$original = $content

		foreach ($package in $packages)
		{
			# csproj:                  <PackageReference Include="X" Version="1.2.3" />
			# Directory.Packages.props: <PackageVersion   Include="X" Version="1.2.3" />
			# The pattern keys on the attributes rather than the element name, so it
			# matches both.
			$pattern = '(Include="' + [regex]::Escape($package) + '"\s+Version=")([^"]+)(")'
			foreach ($match in [regex]::Matches($content, $pattern))
			{
				if ($match.Groups[2].Value -eq $Version) { continue }
				$references += [pscustomobject]@{
					file = $file
					package = $package
					beforeVersion = $match.Groups[2].Value
					afterVersion = $Version
					before = $match.Value
					after = $match.Groups[1].Value + $Version + $match.Groups[3].Value
				}
			}

			# dotnet-tools.json: "package-id": { "version": "1.2.3", ... }
			$toolPattern = '("' + [regex]::Escape($package.ToLowerInvariant()) + '":\s*\{\s*"version":\s*")([^"]+)(")'
			foreach ($match in [regex]::Matches($content, $toolPattern))
			{
				if ($match.Groups[2].Value -eq $Version) { continue }
				$references += [pscustomobject]@{
					file = $file
					package = $package
					beforeVersion = $match.Groups[2].Value
					afterVersion = $Version
					before = $match.Value
					after = $match.Groups[1].Value + $Version + $match.Groups[3].Value
				}
			}
		}

		foreach ($reference in ($references | Where-Object { $_.file -eq $file }))
		{
			$content = $content.Replace($reference.before, $reference.after)
		}

		if ($content -ne $original)
		{
			Write-TextFile -Path $file -Text $content -HasBom $textFile.HasBom
		}
	}

	if ($references.Count -eq 0)
	{
		throw "Nothing here references those packages, so there is nothing to switch."
	}

	foreach ($reference in $references)
	{
		Write-Host "  $($reference.package): $($reference.beforeVersion)  ->  $($reference.afterVersion)"
	}

	# The repository's NuGet.Config clears inherited sources, so the feed has to be named here
	# rather than in a user-level config.
	$nugetConfig = Join-Path $repoRoot 'NuGet.Config'
	if (Test-Path $nugetConfig)
	{
		$configFile = Read-TextFile -Path $nugetConfig
		$config = $configFile.Text
		if ($config -notmatch [regex]::Escape($sourceName))
		{
			$addition = '    <add key="' + $sourceName + '" value="' + $Feed + '" />'
			$config = [regex]::Replace($config, '(\s*)<clear\s*/>', "`$1<clear />`$1$($addition.Trim())", 1)
			Write-TextFile -Path $nugetConfig -Text $config -HasBom $configFile.HasBom
		}
	}

	@{ references = $references } | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -NoNewline

	Write-Host ""
	Write-Host "[use:packages] OK: on local packages at $Version"
	Write-Host "[use:packages] Run ``dotnet tool restore`` before building."
	Write-Host "[use:packages] Undo with: task use:local -- released"
}
catch
{
	Write-Host ""
	Write-Host "[use:packages] FAILED: $($_.Exception.Message)" -ForegroundColor Red
	exit 1
}
finally
{
	Pop-Location
}
