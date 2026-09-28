using System.Globalization;

namespace Mokaterm.Core.Import;

/// <summary>
/// One saved session as a name to value bag. PuTTY and WinSCP keep their sessions in the registry and in text
/// files with the same names, so both readers fill this and one mapper reads it.
/// </summary>
internal sealed class SessionValues
{
	private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

	public SessionValues(string name) => Name = name;

	/// <summary>The session name as the other client shows it, already decoded.</summary>
	public string Name { get; }

	public int Count => _values.Count;

	public void Set(string name, string value) => _values[name] = value;

	public string? Text(string name) =>
		_values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

	public int? Number(string name) =>
		_values.TryGetValue(name, out string? value)
		&& int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
			? parsed
			: null;
}
