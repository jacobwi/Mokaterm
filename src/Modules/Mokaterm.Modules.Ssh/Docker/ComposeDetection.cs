namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Which composes the server has: the plugin, the standalone program, both or neither, each with its version. Every
/// compose command is built from the one <see cref="Pick"/> chooses, so a session never mixes the two.
/// </summary>
internal sealed record ComposeDetection(ComposeTool? Plugin, ComposeTool? Standalone)
{
	public static ComposeDetection None { get; } = new(null, null);

	public bool HasAny => Plugin is not null || Standalone is not null;

	/// <summary>
	/// The compose to run: the plugin first under <see cref="ComposePreference.Auto"/>, and exactly the one asked for
	/// otherwise, or null when that one is not there. A forced choice never falls back, so the command shown is the one
	/// the user picked.
	/// </summary>
	public ComposeTool? Pick(ComposePreference preference) => preference switch
	{
		ComposePreference.Plugin => Plugin,
		ComposePreference.Standalone => Standalone,
		_ => Plugin ?? Standalone,
	};
}
