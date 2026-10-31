namespace Mokaterm.UI.Common.PluginUi;

/// <summary>
/// One labeled fact in a details list: a value, or several (a name and its aliases), shown in mono when it is data and
/// with a copy button when it is worth one.
/// </summary>
public sealed record Fact(string Label, IReadOnlyList<string> Values, bool Mono = false, bool Copyable = false, string? Note = null)
{
	public Fact(string label, string value, bool mono = false, bool copyable = false)
		: this(label, [value], mono, copyable)
	{
	}
}
