namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>One line of <c>/proc/mounts</c>.</summary>
internal sealed record MountEntry(string Source, string MountPoint, string Type, string Options)
{
	public bool IsReadOnly => Options.Split(',').Contains("ro");
}
