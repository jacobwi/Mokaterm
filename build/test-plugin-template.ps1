<#
.SYNOPSIS
	Checks the plugin template the way an outside author meets it: packed, installed, generated, built and loaded.

.DESCRIPTION
	Packs Mokaterm.Abstractions, Mokaterm.UI.Common, Mokaterm.Plugin.Sdk and Mokaterm.Plugin.Templates into a local
	feed with one test version, installs the template into a template hive of its own, and generates one plugin of every
	kind next to a nuget.config that names that feed and nuget.org. Each one has to build with warnings as errors and
	reference exactly the packed version. Then every .mokaplugin it built is opened (plugin.json, lib, wwwroot, themes)
	and loaded through the real plugin loader, from Mokaterm.Core and Mokaterm.UI.Common built from this checkout, the
	way a host loads an operator's plugins folder.

	Everything goes into one work folder, which is deleted at the end: the feed, the template hive, the build output of
	the repository projects (artifacts layout, so the checkout's own bin and obj folders are left alone) and the NuGet
	packages folder the generated plugins restore into, which is the one place the test version of the Mokaterm
	packages ever lands. The user's template hive is never used. The repository projects restore into the usual NuGet
	cache, which holds the third-party packages any build of this repository puts there and nothing else.

.PARAMETER Version
	The version to pack with. Defaults to VersionPrefix in Directory.Build.props with a -templatetest.1 label. The
	generated plugins must reference exactly this version, and their plugin.json must carry its major.minor, which is
	also what the loader built here reports as its own.

.PARAMETER WorkDirectory
	Where everything goes; it must not exist yet. Defaults to a new folder under the temp folder. It has to be outside
	the repository, so the repository's Directory.Build.props and Directory.Packages.props do not reach the plugins.

.PARAMETER KeepWorkDirectory
	Leaves the work folder in place, to look at what was generated.

.EXAMPLE
	./build/test-plugin-template.ps1
	./build/test-plugin-template.ps1 -Version 0.2.0-local.1 -KeepWorkDirectory
#>
[CmdletBinding()]
param(
	[string] $Version,
	[string] $WorkDirectory,
	[switch] $KeepWorkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
	$props = [xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))
	$node = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
	if ($null -eq $node) { throw 'Directory.Build.props has no VersionPrefix; pass -Version.' }
	$Version = "$($node.InnerText.Trim())-templatetest.1"
}

if (-not ($Version -match '^(\d+)\.(\d+)\.\d+(-[0-9A-Za-z.-]+)?$')) {
	throw "Version '$Version' is not MAJOR.MINOR.PATCH with an optional prerelease label."
}

# What plugin.json's sdk has to say: the major.minor of the Mokaterm.Abstractions the plugin compiled against.
$expectedSdk = "$($Matches[1]).$($Matches[2])"

if (-not $WorkDirectory) {
	$WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('mokaterm-plugin-template-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}

$work = [IO.Path]::GetFullPath($WorkDirectory)
$repoFull = [IO.Path]::GetFullPath($repo).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if ($work.StartsWith($repoFull, [StringComparison]::OrdinalIgnoreCase)) {
	throw "The work folder $work is inside the repository, where its build files would reach the generated plugins."
}

if (Test-Path $work) { throw "The work folder $work already exists; name a new one." }

$feed = Join-Path $work 'feed'
$hive = Join-Path $work 'hive'
$artifacts = Join-Path $work 'artifacts'
$consumers = Join-Path $work 'plugins'
$packages = Join-Path $work 'packages'
$loader = Join-Path $work 'loader'
$loadRoot = Join-Path $work 'load'

# No MSBuild node or compiler server outlives a build, since one would hold files in the work folder open.
$noServers = @('-nodeReuse:false', '-p:UseSharedCompilation=false', '-nologo')

# The repository projects build into the work folder in the artifacts layout, with the test version.
$repoBuild = @('-c', 'Release', "-p:Version=$Version", '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifacts") + $noServers

$hiveArguments = @('--debug:custom-hive', $hive)

# One per kind. Publisher and description carry the characters an MSBuild property and an XML element both treat
# specially; the system page shows the id worked out from a name with an underscore; the theme sets its own id.
$cases = @(
	@{
		Name = 'Acme.PasswordTools'
		Arguments = @('--kind', 'tool', '--publisher', 'Acme & Sons', '--description', 'Makes 100% random passwords <fast>, $(NoSuchProperty) and all.')
		Id = 'acme.passwordtools'
		Publisher = 'Acme & Sons'
		Description = 'Makes 100% random passwords <fast>, $(NoSuchProperty) and all.'
		Hosts = @('desktop', 'web')
		HasCode = $true
		Themes = 0
		Registers = @('app-tool password-generator component=Acme.PasswordTools.PasswordGeneratorTool')
	},
	@{
		Name = 'Acme.Kernel_Params'
		Arguments = @('--kind', 'system-page', '--hosts', 'desktop')
		Id = 'acme.kernel-params'
		Publisher = 'Acme'
		Description = 'Adds a Kernel parameters page to the System view.'
		Hosts = @('desktop')
		HasCode = $true
		Themes = 0
		Registers = @('system-page kernel-parameters component=Acme.Kernel_Params.KernelParametersPage')
	},
	@{
		Name = 'Acme.Themes'
		Arguments = @('--kind', 'theme', '--hosts', 'web', '--id', 'acme.night-themes')
		Id = 'acme.night-themes'
		Publisher = 'Acme'
		Description = 'Terminal themes for Mokaterm.'
		Hosts = @('web')
		HasCode = $false
		Themes = 2
		Registers = @()
	}
)

# The loader check: Mokaterm.Core and Mokaterm.UI.Common from this checkout stand in for a host, as in
# tests/Mokaterm.Plugins.Tests, and only their public API is used. A package goes into an operator folder (nothing to
# trust) for a host it supports: web when it names web or no host, the desktop otherwise.
$loaderProject = @'
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net10.0</TargetFramework>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="{Repo}/src/Mokaterm.Core/Mokaterm.Core.csproj" />
		<ProjectReference Include="{Repo}/src/UI/Mokaterm.UI.Common/Mokaterm.UI.Common.csproj" />
	</ItemGroup>
</Project>
'@

$loaderProgram = @'
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Plugins;
using Mokaterm.UI.Common.Contributions;

string root = Path.GetFullPath(args[0]);
Console.WriteLine($"host-sdk {PluginCompatibility.HostSdk.Major}.{PluginCompatibility.HostSdk.Minor}");
Dictionary<HostKind, string> folders = [];
foreach (string package in args.Skip(1))
{
	string id;
	List<string> hosts = [];
	using (ZipArchive zip = ZipFile.OpenRead(package))
	{
		using Stream manifest = zip.GetEntry("plugin.json")!.Open();
		using JsonDocument json = JsonDocument.Parse(manifest);
		id = json.RootElement.GetProperty("id").GetString()!;
		if (json.RootElement.TryGetProperty("hosts", out JsonElement names))
		{
			hosts.AddRange(names.EnumerateArray().Select(name => name.GetString()!));
		}
	}

	HostKind host = hosts.Count == 0 || hosts.Contains("web") ? HostKind.Web : HostKind.Desktop;
	string folder = Path.Combine(root, host.ToString().ToLowerInvariant());
	folders[host] = folder;
	ZipFile.ExtractToDirectory(package, Path.Combine(folder, id));
}

int failed = 0;
foreach ((HostKind host, string folder) in folders)
{
	PluginCatalog catalog = PluginCatalog.Open(new PluginCatalogOptions { Directory = folder, Host = host });
	catalog.ConfirmStarted();
	ServiceCollection services = new();
	new MokatermBuilder(services).AddPlugins(catalog);

	foreach (PluginRecord record in catalog.Records)
	{
		Console.WriteLine($"plugin {record.Id} host={host} status={record.Status} code={record.HasCode} themes={record.ThemeCount} problem={record.Problem}");
		failed += record.Status == PluginStatus.Enabled ? 0 : 1;
	}

	foreach (ServiceDescriptor descriptor in services.Where(descriptor => !descriptor.IsKeyedService))
	{
		string? line = descriptor.ImplementationInstance switch
		{
			ModuleInfo module => $"module {module.Id} version={module.Version}",
			AppToolDescriptor tool => $"app-tool {tool.Id} component={tool.Component.FullName}",
			SystemPageDescriptor page => $"system-page {page.Id} component={page.Component.FullName}",
			SettingsPageDescriptor page => $"settings-page {page.Id} group={page.Group} rows={SettingsSearchIndex.For(page.Component).Count}",
			SettingsSectionType section => $"settings-section {section.Key} type={section.Type.FullName}",
			_ => null,
		};
		if (line is not null)
		{
			Console.WriteLine(line);
		}
	}
}

return failed;
'@

function Invoke-Dotnet {
	param(
		[Parameter(Mandatory = $true)] [string] $What,
		[Parameter(Mandatory = $true)] [string[]] $Arguments
	)

	Write-Host ''
	Write-Host "== $What"
	& dotnet @Arguments
	if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE." }
}

function Write-Utf8File {
	param([string] $Path, [string] $Text)

	New-Item -ItemType Directory -Force (Split-Path -Parent $Path) | Out-Null
	[IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# Empty build files stop MSBuild from looking further up for ones that are not the author's.
function Write-BuildFenceFiles {
	param([string] $Folder)

	foreach ($name in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
		Write-Utf8File (Join-Path $Folder $name) "<Project>`r`n</Project>`r`n"
	}
}

function Assert-That {
	param([bool] $Condition, [string] $Problem)

	if (-not $Condition) { throw $Problem }
}

function Read-PluginPackage {
	param([string] $Path)

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip = [IO.Compression.ZipFile]::OpenRead($Path)
	try {
		$entries = @($zip.Entries | ForEach-Object { $_.FullName })
		$reader = New-Object IO.StreamReader($zip.GetEntry('plugin.json').Open())
		try {
			$manifest = $reader.ReadToEnd() | ConvertFrom-Json
		}
		finally {
			$reader.Dispose()
		}
	}
	finally {
		$zip.Dispose()
	}

	return @{ Entries = $entries; Manifest = $manifest }
}

function Get-ManifestValue {
	param($Manifest, [string] $Name)

	$property = $Manifest.PSObject.Properties[$Name]
	if ($null -eq $property) { return $null }
	return $property.Value
}

# The script runs in the caller's session, so whatever it sets in the environment is put back afterwards.
$savedEnvironment = @{}
foreach ($name in @('NUGET_PACKAGES', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')) {
	$savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$succeeded = $false
try {
	New-Item -ItemType Directory -Force $work | Out-Null
	Write-Host "Checking the plugin template with version $Version in $work"

	foreach ($project in @(
			'src\Mokaterm.Abstractions\Mokaterm.Abstractions.csproj',
			'src\UI\Mokaterm.UI.Common\Mokaterm.UI.Common.csproj',
			'src\Mokaterm.Plugin.Sdk\Mokaterm.Plugin.Sdk.csproj',
			'templates\Mokaterm.Plugin.Templates\Mokaterm.Plugin.Templates.csproj')) {
		Invoke-Dotnet "Packing $(Split-Path -Leaf $project)" (@('pack', (Join-Path $repo $project), '-o', $feed) + $repoBuild)
	}

	$templatePackage = Join-Path $feed "Mokaterm.Plugin.Templates.$Version.nupkg"
	Assert-That (Test-Path $templatePackage) "The pack wrote no $templatePackage."
	Invoke-Dotnet 'Installing the template into its own hive' (@('new', 'install', $templatePackage) + $hiveArguments)

	Write-BuildFenceFiles $consumers
	Write-Utf8File (Join-Path $consumers 'nuget.config') @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="local" value="$feed" />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
	</packageSources>
	<packageSourceMapping>
		<packageSource key="local">
			<package pattern="Mokaterm.*" />
		</packageSource>
		<packageSource key="nuget.org">
			<package pattern="*" />
		</packageSource>
	</packageSourceMapping>
</configuration>
"@

	$built = @()
	$env:NUGET_PACKAGES = $packages
	try {
		foreach ($case in $cases) {
			$folder = Join-Path $consumers $case.Name
			Invoke-Dotnet "Generating $($case.Name)" (@('new', 'mokaterm-plugin', '-n', $case.Name, '-o', $folder, '--no-update-check') + $case.Arguments + $hiveArguments)

			$project = [xml](Get-Content -Raw (Join-Path $folder "$($case.Name).csproj"))
			$references = @($project.SelectNodes('//PackageReference') | Where-Object { $_.GetAttribute('Include') -like 'Mokaterm.*' })
			$expectedReferences = 1
			if ($case.HasCode) { $expectedReferences = 2 }
			Assert-That ($references.Count -eq $expectedReferences) "$($case.Name) references $($references.Count) Mokaterm packages, not $expectedReferences."
			foreach ($reference in $references) {
				Assert-That ($reference.GetAttribute('Version') -eq $Version) "$($case.Name) references $($reference.GetAttribute('Include')) $($reference.GetAttribute('Version')), not the packed $Version."
			}

			Invoke-Dotnet "Building $($case.Name)" (@('build', $folder, '-warnaserror') + $noServers)

			$package = Join-Path $folder "bin\Debug\net10.0\$($case.Id)-1.0.0.mokaplugin"
			Assert-That (Test-Path $package) "Building $($case.Name) wrote no $package."
			$built += $package
		}
	}
	finally {
		$env:NUGET_PACKAGES = $savedEnvironment['NUGET_PACKAGES']
	}

	Write-Host ''
	Write-Host '== Reading the packages'
	for ($index = 0; $index -lt $cases.Count; $index++) {
		$case = $cases[$index]
		$read = Read-PluginPackage $built[$index]
		$manifest = $read.Manifest
		$entries = $read.Entries
		$name = $case.Name

		Assert-That ((Get-ManifestValue $manifest 'id') -eq $case.Id) "$name's plugin.json has id '$(Get-ManifestValue $manifest 'id')'."
		Assert-That ((Get-ManifestValue $manifest 'name') -eq $name) "$name's plugin.json has name '$(Get-ManifestValue $manifest 'name')'."
		Assert-That ((Get-ManifestValue $manifest 'version') -eq '1.0.0') "$name's plugin.json has version '$(Get-ManifestValue $manifest 'version')'."
		Assert-That ((Get-ManifestValue $manifest 'publisher') -eq $case.Publisher) "$name's plugin.json has publisher '$(Get-ManifestValue $manifest 'publisher')'."
		Assert-That ((Get-ManifestValue $manifest 'description') -eq $case.Description) "$name's plugin.json has description '$(Get-ManifestValue $manifest 'description')'."
		Assert-That ((Get-ManifestValue $manifest 'sdk') -eq $expectedSdk) "$name's plugin.json has sdk '$(Get-ManifestValue $manifest 'sdk')', not $expectedSdk."
		Assert-That ((@(Get-ManifestValue $manifest 'hosts') -join ';') -eq ($case.Hosts -join ';')) "$name's plugin.json has hosts '$(@(Get-ManifestValue $manifest 'hosts') -join ';')'."

		$lib = @($entries | Where-Object { $_ -like 'lib/*' })
		$webRoot = @($entries | Where-Object { $_ -like 'wwwroot/*' })
		$themes = @($entries | Where-Object { $_ -like 'themes/*' })
		$other = @($entries | Where-Object { $_ -ne 'plugin.json' -and $_ -notlike 'lib/*' -and $_ -notlike 'wwwroot/*' -and $_ -notlike 'themes/*' })
		Assert-That ($other.Count -eq 0) "$name's package holds $($other -join ', ') outside the four places a package may use."
		Assert-That ($themes.Count -eq $case.Themes) "$name's package holds $($themes.Count) themes, not $($case.Themes)."

		if ($case.HasCode) {
			$assembly = "$name.dll"
			Assert-That ((Get-ManifestValue $manifest 'assembly') -eq $assembly) "$name's plugin.json names assembly '$(Get-ManifestValue $manifest 'assembly')', not $assembly."
			Assert-That ($lib -contains "lib/$assembly") "$name's package has no lib/$assembly."

			# Everything the host provides stays out: the plugin's own assembly, symbols and deps.json are all lib holds.
			$foreign = @($lib | Where-Object { $_ -notlike "lib/$name.*" })
			Assert-That ($foreign.Count -eq 0) "$name's lib holds files that are not the plugin's own: $($foreign -join ', ')."
			Assert-That (($webRoot.Count -eq 1) -and ($webRoot[0] -eq "wwwroot/$name.bundle.scp.css")) "$name's wwwroot holds '$($webRoot -join ', ')', not just its scoped style bundle."
		}
		else {
			Assert-That ($null -eq (Get-ManifestValue $manifest 'assembly')) "$name is data only, but its plugin.json names an assembly."
			Assert-That (($lib.Count -eq 0) -and ($webRoot.Count -eq 0)) "$name is data only, but its package holds code or page files."
		}

		Write-Host "  $($case.Id): plugin.json, $($lib.Count) lib files, $($webRoot.Count) page files, $($themes.Count) themes"
	}

	Write-BuildFenceFiles $loader
	Write-Utf8File (Join-Path $loader 'PluginLoadCheck.csproj') $loaderProject.Replace('{Repo}', $repo)
	Write-Utf8File (Join-Path $loader 'Program.cs') $loaderProgram
	Invoke-Dotnet 'Building the loader check' (@('build', (Join-Path $loader 'PluginLoadCheck.csproj')) + $repoBuild)

	Write-Host ''
	Write-Host '== Loading the packages'
	$loaderDll = Join-Path $artifacts 'bin\PluginLoadCheck\release\PluginLoadCheck.dll'
	$lines = @(& dotnet $loaderDll $loadRoot @built)
	$loaderExit = $LASTEXITCODE
	$lines | ForEach-Object { Write-Host "  $_" }
	Assert-That ($loaderExit -eq 0) "The loader did not turn every plugin on (exit code $loaderExit)."
	Assert-That ($lines -contains "host-sdk $expectedSdk") "The loader built here is not Mokaterm $expectedSdk, so it cannot vouch for the packages' sdk."

	foreach ($case in $cases) {
		$id = $case.Id
		$pattern = '^plugin ' + [regex]::Escape($id) + ' host=\w+ status=Enabled code=' + $case.HasCode + ' themes=' + $case.Themes + ' problem=$'
		Assert-That (@($lines | Where-Object { $_ -match $pattern }).Count -eq 1) "The loader did not turn $id on as expected."

		$expected = @() + $case.Registers
		if ($case.HasCode) {
			$expected += "module $id version=1.0.0"
			$expected += "settings-page plugin.$id group=Plugins rows=2"
			$expected += "settings-section plugin.$id type=$($case.Name).PluginSettings"
		}

		foreach ($line in $expected) {
			Assert-That ($lines -contains $line) "The loader did not register '$line'."
		}
	}

	$succeeded = $true
	Write-Host ''
	Write-Host "The template generated, built, packed and loaded every kind with Mokaterm $Version."
}
finally {
	foreach ($name in $savedEnvironment.Keys) {
		[Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
	}

	if ($KeepWorkDirectory) {
		Write-Host "Kept $work"
	}
	elseif (Test-Path $work) {
		# A file still held for a moment by a process that just ended (or a virus scanner) fails the first delete.
		for ($attempt = 1; $attempt -le 5; $attempt++) {
			try {
				Remove-Item -Recurse -Force $work
				break
			}
			catch {
				if ($attempt -eq 5) {
					Write-Warning "Could not delete $work`: $($_.Exception.Message)"
				}
				else {
					Start-Sleep -Seconds 2
				}
			}
		}
	}

	if (-not $succeeded) { Write-Host 'The plugin template check failed.' }
}
