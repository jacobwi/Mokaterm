#Requires -Version 5
<#
	Packs what plugin authors build against into artifacts/nuget: the contracts (Mokaterm.Abstractions), the shared UI
	(Mokaterm.UI.Common), the plugin SDK (Mokaterm.Plugin.Sdk) and the project template. Pushing them is the release
	workflow's step, never this script's.

	-Version stamps every package and the assemblies inside it. Plugins record the major.minor of the Mokaterm.Abstractions
	they compiled against, and the app loads only plugins built for its own, so packages for a release must carry that
	release's version. Without -Version the packages take VersionPrefix from Directory.Build.props.

	Runs in Windows PowerShell 5.1, like the other scripts here.
#>
param(
	[string]$Version,
	[string]$Output
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Output) {
	$Output = Join-Path $root 'artifacts\nuget'
}

$projects = @(
	'src\Mokaterm.Abstractions\Mokaterm.Abstractions.csproj',
	'src\UI\Mokaterm.UI.Common\Mokaterm.UI.Common.csproj',
	'src\Mokaterm.Plugin.Sdk\Mokaterm.Plugin.Sdk.csproj',
	'src\Mokaterm.Plugin.TestHost\Mokaterm.Plugin.TestHost.csproj',
	'templates\Mokaterm.Plugin.Templates\Mokaterm.Plugin.Templates.csproj'
)

# A stale package from an earlier run would be pushed along with the new ones.
if (Test-Path $Output) {
	Remove-Item -Recurse -Force $Output
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null

foreach ($project in $projects) {
	$path = Join-Path $root $project
	if (-not (Test-Path $path)) {
		throw "$project is missing."
	}

	$arguments = @('pack', $path, '--configuration', 'Release', '--output', $Output, '-nologo')
	if ($Version) {
		$arguments += "-p:Version=$Version"
	}

	& dotnet @arguments
	if ($LASTEXITCODE -ne 0) {
		throw "Packing $project failed."
	}
}

$packages = @(Get-ChildItem -Path $Output -Filter '*.nupkg')
if ($packages.Count -ne $projects.Count) {
	throw "Expected $($projects.Count) packages in $Output, found $($packages.Count)."
}

$packages | ForEach-Object { Write-Host $_.Name }
