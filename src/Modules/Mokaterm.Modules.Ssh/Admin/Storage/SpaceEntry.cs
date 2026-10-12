namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>A folder du measured, or a file the large file search found, with its size in KiB.</summary>
internal sealed record SpaceEntry(string Path, long Kb)
{
	/// <summary>The last part of <see cref="Path"/>, which is what a row shows under its folder.</summary>
	public string Name => SpaceCommands.NameOf(Path);
}
