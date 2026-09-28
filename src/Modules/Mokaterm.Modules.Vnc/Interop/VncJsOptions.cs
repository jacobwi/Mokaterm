namespace Mokaterm.Modules.Vnc.Interop;

/// <summary>Everything vnc.js needs to build an RFB instance, in the shape and names the module reads.</summary>
internal sealed record VncJsOptions
{
	public required bool Shared { get; init; }

	public required bool ViewOnly { get; init; }

	/// <summary>False while the session's tab is hidden, which also releases every key held on the remote side.</summary>
	public required bool Active { get; init; }

	/// <summary><c>fit</c>, <c>actual</c> or <c>remote</c>.</summary>
	public required string Scaling { get; init; }

	public required int Quality { get; init; }

	public required int Compression { get; init; }

	public required bool ShowDotCursor { get; init; }

	/// <summary>Longest clipboard text, in characters, the page keeps when the session copies something.</summary>
	public required int ClipboardLimit { get; init; }

	/// <summary>CSS colour behind the screen, so the letterbox around it matches the theme.</summary>
	public required string Background { get; init; }
}
