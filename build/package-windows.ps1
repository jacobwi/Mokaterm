<#
.SYNOPSIS
	Builds the Windows installer for the desktop app.

.DESCRIPTION
	Publishes Mokaterm.Maui for net10.0-windows10.0.19041.0, self-contained for win-x64 (the .NET runtime and the Windows
	App SDK ship inside the app), and packs it with Velopack. artifacts/windows then holds Setup.exe, a portable zip and
	the release files a later update feed is built from. WebView2 is the one thing Setup may still have to install: it
	checks for the Evergreen runtime and fetches it when a machine lacks it.

	Setup.exe installs for the current user into %LOCALAPPDATA%\Moka.Mokaterm, needs no administrator, and adds a Start
	menu and a desktop shortcut. Uninstalling deletes that folder only. The app keeps its settings and vault in
	%LOCALAPPDATA%\Mokaterm, which no uninstaller touches (see Assert-NotDataFolder). While it installs, Setup shows a
	splash that make-installer-splash.ps1 draws for the version being packed.

	The build is NOT signed. On other machines SmartScreen warns before Setup.exe runs, and Smart App Control, where it is
	on, blocks it. Pass -AzureTrustedSignFile once a certificate exists.

.PARAMETER Version
	The package version, SemVer 2. Defaults to VersionPrefix in Directory.Build.props. A prerelease label (0.2.0-beta.1)
	stays on the package and is left out of the Windows file version, which cannot carry one.

.PARAMETER AzureTrustedSignFile
	Path to an Azure Trusted Signing metadata.json. vpk then signs the app's files, Setup.exe and the portable launcher.

.PARAMETER GitHubRepoUrl
	A GitHub repository holding earlier releases of this channel. The previous release is downloaded into the output
	folder before packing, which is what lets vpk build a delta package against it; without one every update is a full
	download. A repository with no release yet is not an error.

.PARAMETER GitHubToken
	Token for -GitHubRepoUrl. Needed for a private repository; in Actions pass the job's own github.token.

.PARAMETER KeepWindowsAi
	Keeps the Windows App SDK's machine learning runtime (onnxruntime.dll, onnxruntime_providers_shared.dll and
	DirectML.dll, about 40 MB) in the package. It is left out by default because Mokaterm never calls Windows AI or
	Windows ML; use this switch if a build without it ever misbehaves.

.EXAMPLE
	./build/package-windows.ps1
	./build/package-windows.ps1 -Version 0.2.0-beta.1
#>
[CmdletBinding()]
param(
	[string] $Version,
	[string] $AzureTrustedSignFile,
	[string] $GitHubRepoUrl,
	[string] $GitHubToken,
	[switch] $KeepWindowsAi
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Hosts\Mokaterm.Maui\Mokaterm.Maui.csproj'
# The exe's own icon (build/make-app-icon.ps1), not MAUI's appicon.ico in the publish output, which holds one 64x64 image.
$icon = Join-Path $repo 'src\Hosts\Mokaterm.Maui\Platforms\Windows\mokaterm.ico'
$framework = 'net10.0-windows10.0.19041.0'
$runtime = 'win-x64'
$publishDir = Join-Path $repo 'artifacts\publish\windows'
$outputDir = Join-Path $repo 'artifacts\windows'
# Outside the publish folder, or the splash would ship inside the app.
$splash = Join-Path $repo 'artifacts\installer\splash.png'
# Setup's progress bar, the Moka accent.
$splashProgressColor = '#ef5350'

# The package id names the install folder and ties every future update to this install: changing it later means users
# reinstall. It must never be the data folder's name (checked below).
$packId = 'Moka.Mokaterm'
$packTitle = 'Mokaterm'
$packAuthors = 'Moka'
$mainExe = 'Mokaterm.Maui.exe'

# The update channel; a feed for this build later serves releases.win.json.
$channel = 'win'

# DesktopPaths.FolderName: the app keeps its settings and vault in %LOCALAPPDATA%\Mokaterm.
$dataFolderName = 'Mokaterm'

# Velopack works on whole folders by name. Setup installs into %LOCALAPPDATA%\<package id> and its uninstaller deletes
# that folder; an install whose folder is not writable keeps Update.exe and its packages in %LOCALAPPDATA%\<package id>
# instead. That name may never be the folder that holds the vault.
function Assert-NotDataFolder([string] $Name, [string] $What) {
	# Windows ignores trailing dots and spaces in a folder name and compares names without regard to case.
	if ($Name.TrimEnd('.', ' ') -ieq $dataFolderName) {
		throw "The $What '$Name' names the data folder %LOCALAPPDATA%\$dataFolderName; an uninstall would delete the vault."
	}
}

Assert-NotDataFolder $packId 'package id'

# No MSI (vpk's --msi), on purpose. Velopack 1.2.0's MSI uninstall runs a cleanup that deletes %LOCALAPPDATA%\<title>
# recursively: its WiX template passes the pack title where the package id is meant (RustAppId = AppTitle). With the
# title Mokaterm that folder is the vault. Setup.exe's uninstaller only removes its own install folder. Before adding
# --msi, check that Velopack's MsiTemplate.hbs uses the id there and run Assert-NotDataFolder on the title as well.

if (-not $Version) {
	$props = [xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))
	$node = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
	if ($null -eq $node) { throw 'Directory.Build.props has no VersionPrefix; pass -Version.' }
	$Version = $node.InnerText.Trim()
}

if ($Version -notmatch '^(\d+\.\d+\.\d+)(?:-([0-9A-Za-z][0-9A-Za-z.-]*))?$') {
	throw "Version '$Version' is not MAJOR.MINOR.PATCH with an optional -label."
}

$versionPrefix = $Matches[1]
$versionSuffix = $Matches[2]

if ($AzureTrustedSignFile) {
	$AzureTrustedSignFile = (Resolve-Path $AzureTrustedSignFile).Path
}
else {
	Write-Warning ('This build is NOT signed. On other machines SmartScreen warns before Setup.exe runs, and Smart App ' +
		'Control, where it is on, blocks it. Pass -AzureTrustedSignFile to sign once a certificate exists.')
}

Push-Location $repo
try {
	Write-Host "Publishing $mainExe $Version for $runtime..."
	if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

	# RuntimeIdentifierOverride instead of -r: Microsoft's workaround for a Windows App SDK publishing bug (the project
	# turns it into RuntimeIdentifier). .NET 10 takes only portable identifiers such as win-x64.
	$publishArgs = @(
		'publish', $project,
		'-f', $framework,
		'-c', 'Release',
		"-p:RuntimeIdentifierOverride=$runtime",
		'-p:SelfContained=true',
		'-p:WindowsAppSDKSelfContained=true',
		'-p:WindowsPackageType=None',
		"-p:VersionPrefix=$versionPrefix",
		'-o', $publishDir,
		'-nologo'
	)
	if ($versionSuffix) { $publishArgs += "-p:VersionSuffix=$versionSuffix" }

	& dotnet @publishArgs
	if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

	# Windows App SDK 1.8 ships its machine learning runtime with every self-contained app and has no switch to leave it
	# out (WindowsAppSDK issue 5969). Only the native files go: the SDK's self-contained payload copies them and the app's
	# deps.json does not list them, so the .NET host never looks for them. The small managed projections that deps.json
	# does list stay, because a listed assembly missing on disk stops the app from starting.
	if (-not $KeepWindowsAi) {
		$removed = 0
		foreach ($name in @('onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'DirectML.dll')) {
			$file = Join-Path $publishDir $name
			if (Test-Path $file) {
				$removed += (Get-Item $file).Length
				Remove-Item $file
			}
		}

		Write-Host ("Left out the Windows ML runtime ({0:N1} MB); -KeepWindowsAi keeps it." -f ($removed / 1MB))
	}

	if (-not (Test-Path (Join-Path $publishDir $mainExe))) { throw "The publish output has no $mainExe." }
	if (-not (Test-Path $icon)) { throw "$icon is missing; build/make-app-icon.ps1 draws it." }

	# The splash Setup shows while it installs, drawn for this version. WPF draws it and needs a single-threaded
	# apartment, so it runs in Windows PowerShell with -STA whichever PowerShell runs this script.
	$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
	& $windowsPowerShell -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-installer-splash.ps1') -Version $Version -OutFile $splash
	if ($LASTEXITCODE -ne 0 -or -not (Test-Path $splash)) { throw 'Drawing the installer splash failed.' }

	# vpk reads every option from VPK_* variables as well; one left in the environment (VPK_MSI, say) would change the
	# package behind this script's back.
	Get-ChildItem Env: | Where-Object { $_.Name -like 'VPK_*' } | ForEach-Object { Remove-Item "Env:$($_.Name)" }

	& dotnet tool restore
	if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

	# A fresh folder each time, then the previous release back into it: vpk builds a delta against whatever full
	# package it finds there, so an empty folder means every user downloads the whole app again.
	if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }

	if ($GitHubRepoUrl) {
		Write-Host "Fetching the previous $channel release from $GitHubRepoUrl..."
		$downloadArgs = @('vpk', 'download', 'github', '--repoUrl', $GitHubRepoUrl, '--channel', $channel, '--outputDir', $outputDir)
		if ($GitHubToken) { $downloadArgs += @('--token', $GitHubToken) }
		& dotnet @downloadArgs
		# The first release has nothing to build a delta against, and a feed that cannot be read is not worth failing a
		# release over: the full package alone still installs and still updates.
		if ($LASTEXITCODE -ne 0) {
			Write-Warning 'No previous release was downloaded, so this package carries no delta.'
			$global:LASTEXITCODE = 0
		}
	}

	Write-Host "Packing $packId $Version..."
	$packArgs = @(
		'vpk', 'pack',
		'--packId', $packId,
		'--packVersion', $Version,
		'--packTitle', $packTitle,
		'--packAuthors', $packAuthors,
		'--packDir', $publishDir,
		'--mainExe', $mainExe,
		'--icon', $icon,
		'--splashImage', $splash,
		'--splashProgressColor', $splashProgressColor,
		'--runtime', $runtime,
		'--channel', $channel,
		# The only prerequisite a self-contained build still has. Windows 11 ships it; Setup fetches it elsewhere.
		'--framework', 'webview2',
		'--outputDir', $outputDir
	)
	if ($AzureTrustedSignFile) { $packArgs += @('--azureTrustedSignFile', $AzureTrustedSignFile) }

	& dotnet @packArgs
	if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE." }
}
finally {
	Pop-Location
}

Write-Host ''
Write-Host "Packages in $($outputDir):"
Get-ChildItem $outputDir -File | Sort-Object Length -Descending | ForEach-Object {
	Write-Host ('  {0,-45} {1,10:N1} MB' -f $_.Name, ($_.Length / 1MB))
}

if (-not $AzureTrustedSignFile) {
	Write-Warning 'Unsigned: expect SmartScreen and Smart App Control to stop Setup.exe on machines other than this one.'
}
