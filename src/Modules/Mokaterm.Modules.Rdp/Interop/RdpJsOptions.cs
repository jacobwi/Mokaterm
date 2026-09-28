namespace Mokaterm.Modules.Rdp.Interop;

/// <summary>Everything rdp.js needs to build a screen, in the shape and names the module reads.</summary>
internal sealed record RdpJsOptions
{
	public required int Width { get; init; }

	public required int Height { get; init; }

	/// <summary><c>fit</c>, <c>actual</c> or <c>remote</c>.</summary>
	public required string Scaling { get; init; }

	public required bool ViewOnly { get; init; }

	/// <summary>False while the session's tab is hidden, which also stops the page from sending anything.</summary>
	public required bool Active { get; init; }

	/// <summary>Release what is held on the server when the session's tab is hidden.</summary>
	public required bool ReleaseKeysWhenInactive { get; init; }

	/// <summary>CSS colour behind the screen, so the letterbox around it matches the theme.</summary>
	public required string Background { get; init; }
}
