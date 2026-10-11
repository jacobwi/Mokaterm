<#
.SYNOPSIS
	Builds the Microsoft Store package for the desktop app.

.DESCRIPTION
	Publishes Mokaterm.Maui as MSIX and writes a .msixupload to artifacts/store, which is what Partner Center takes.
	This is a second artifact beside the Velopack installer, not a replacement: the two install side by side, each in
	its own folder, and both read the same vault in %LOCALAPPDATA%\Mokaterm.

	The Store signs the package with its own certificate, so nothing here signs anything and the result shows no
	SmartScreen warning when installed from the Store. The Velopack Setup.exe is a separate question
	(build/package-windows.ps1).

	The packaged build leaves the updater and its settings page out: a Store package is read-only, so Velopack cannot
	replace its files, and the Store is what delivers a new version. That is the MOKATERM_PACKAGED constant in the
	csproj, not a runtime check, because the two builds are separate.

.PARAMETER Version
	The package version, MAJOR.MINOR.PATCH. Defaults to VersionPrefix in Directory.Build.props. The Store takes four
	parts and reserves the last for itself, so this script appends .0 and refuses a prerelease label, which an MSIX
	version cannot carry.

.EXAMPLE
	./build/package-store.ps1
	./build/package-store.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
	[string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Hosts\Mokaterm.Maui\Mokaterm.Maui.csproj'
$manifest = Join-Path $repo 'src\Hosts\Mokaterm.Maui\Platforms\Windows\Package.appxmanifest'
$framework = 'net10.0-windows10.0.19041.0'
$runtime = 'win-x64'
$outputDir = Join-Path $repo 'artifacts\store'

if (-not $Version) {
	$props = [xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))
	$node = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
	if ($null -eq $node) { throw 'Directory.Build.props has no VersionPrefix; pass -Version.' }
	$Version = $node.InnerText.Trim()
}

# An MSIX version is four numbers and carries no label, so a prerelease cannot be published this way at all.
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
	throw "Version '$Version' is not MAJOR.MINOR.PATCH. An MSIX version carries no prerelease label."
}

# Partner Center owns the last part and rejects a package that sets it, so it is always zero here.
$packageVersion = "$Version.0"

# The manifest ships with MAUI's placeholders. Publishing those would be rejected by Partner Center, and worse, a
# wrong identity published once is hard to take back, so this refuses rather than guesses.
[xml] $manifestXml = Get-Content -Raw $manifest
$identity = $manifestXml.Package.Identity
$placeholders = @(
	@{ What = 'Identity Name'; Value = $identity.Name },
	@{ What = 'Identity Publisher'; Value = $identity.Publisher },
	@{ What = 'DisplayName'; Value = $manifestXml.Package.Properties.DisplayName },
	@{ What = 'PublisherDisplayName'; Value = $manifestXml.Package.Properties.PublisherDisplayName }
)
foreach ($entry in $placeholders) {
	if ($entry.Value -match 'placeholder' -or [string]::IsNullOrWhiteSpace($entry.Value)) {
		throw ("$($entry.What) in Package.appxmanifest is still '$($entry.Value)'. Take the real values from " +
			'Partner Center, under the app Product identity page, before building a Store package.')
	}
}

if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }
New-Item -ItemType Directory -Force $outputDir | Out-Null

# MAUI writes the real manifest into obj behind a stamp file, so a build that only changes the version reuses the one
# from last time and the package keeps the old number without saying so. Removing it forces the version through.
Get-ChildItem (Join-Path $repo 'src\Hosts\Mokaterm.Maui\obj') -Recurse -Filter 'Package.appxmanifest' -ErrorAction SilentlyContinue |
	Remove-Item -Force

# AppxSymbolPackageEnabled is off because the symbol package needs mspdbcmf.exe from the Visual Studio C++ tools,
# which a machine with only the .NET SDK does not have. Symbols are optional for a Store submission: they feed crash
# analysis in Partner Center and nothing else.
#
# ApplicationVersion is forced to 0 because MAUI's WinUI targets build the four-part version as
# ApplicationDisplayVersion plus ApplicationVersion, and the csproj sets the latter to 1. Partner Center owns the last
# part and refuses a package that sets it, so a default build produced 0.1.1.1 and would have been rejected.
#
# SelfContained carries the .NET runtime inside the package. A runtime identifier alone no longer implies it, and no
# Store package installs the .NET 10 runtime, so 0.1.4 went out depending on one: certification flagged it under
# 10.2.4.1 (undisclosed software dependencies), and a machine without .NET 10 could not start the app. The Windows App
# SDK stays a framework package, because the Store installs a declared framework dependency by itself.
Write-Host "Publishing $packageVersion for the Store..."
& dotnet publish $project `
	-c Release `
	-f $framework `
	-r $runtime `
	-p:SelfContained=true `
	-p:WindowsPackageType=MSIX `
	-p:GenerateAppxPackageOnBuild=true `
	-p:UapAppxPackageBuildMode=StoreUpload `
	-p:AppxPackageSigningEnabled=false `
	-p:AppxSymbolPackageEnabled=false `
	-p:AppxPackageDir="$outputDir\" `
	-p:ApplicationDisplayVersion=$Version `
	-p:ApplicationVersion=0 `
	-p:Version=$packageVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$upload = Get-ChildItem $outputDir -Recurse -Filter *.msixupload -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $upload) {
	throw "No .msixupload was produced. Check the publish output in $outputDir."
}

Write-Host ''
Write-Host "Store package: $($upload.FullName)"
Write-Host ("  {0:N1} MB" -f ($upload.Length / 1MB))
Write-Host 'Upload it in Partner Center. The Store signs it, so it is unsigned here on purpose.'
