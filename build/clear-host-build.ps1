<#
	Empties the desktop host's Release build folders before a package is published from them.

	The Store package and the GitHub one build the same project into the same Release folders, and the app's resource
	index (Mokaterm.Maui.pri) is kept from whichever build ran last. The Store's leaves WinUI's own resources out,
	because the Windows App SDK framework package carries them there, and a self-contained build that picks that index
	up closes at start inside XamlControlsResources: 0.3.2's first local Setup.exe did exactly that.
#>
$hostDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Hosts\Mokaterm.Maui'
foreach ($folder in @('bin\Release', 'bin\x64\Release', 'obj\Release', 'obj\x64\Release')) {
	$path = Join-Path $hostDir $folder
	if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
