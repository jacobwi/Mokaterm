using System.Text.Json.Serialization;

namespace Mokaterm.UI.Terminal.Interop;

/// <summary>
/// One recording event in the shape terminal.js replays: <see cref="Output"/> for text, otherwise a resize. What an event
/// does not use is left out of the JSON, since a recording can hold a few hundred thousand of them.
/// </summary>
internal sealed record TerminalJsPlayerEvent(
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Output,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Columns,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Rows);
