namespace Mokaterm.Modules.Local.Containers;

/// <summary>The words that start a <see cref="ContainerShell"/> inside a container, after <c>docker exec</c> or <c>kubectl exec --</c>.</summary>
internal static class ContainerShellCommand
{
	/// <summary>
	/// Picks bash where it exists and sh otherwise, inside the container, because only the container knows what it has.
	/// <c>command -v</c> is POSIX, so this runs under any sh: dash, ash and busybox included.
	/// </summary>
	public const string AutoScript = "command -v bash >/dev/null && exec bash || exec sh";

	/// <summary>The program and its arguments, or nothing for <see cref="ContainerShell.Custom"/>, whose text the login typed.</summary>
	public static IReadOnlyList<string> Words(ContainerShell shell) => shell switch
	{
		ContainerShell.Bash => ["bash"],
		ContainerShell.Sh => ["sh"],
		ContainerShell.Custom => [],
		_ => ["sh", "-c", AutoScript],
	};
}
