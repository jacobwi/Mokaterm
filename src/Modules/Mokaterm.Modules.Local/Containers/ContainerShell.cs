namespace Mokaterm.Modules.Local.Containers;

/// <summary>What a container or pod login runs inside it.</summary>
public enum ContainerShell
{
	/// <summary>bash when the image has it, otherwise sh, which every image with a shell has.</summary>
	Auto,

	Bash,

	Sh,

	/// <summary>The command the login names, passed on as typed.</summary>
	Custom,
}
