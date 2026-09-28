using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// Protocol-specific connection settings as an immutable string map. Modules own their keys and wrap
/// them in typed accessors. A flat stored shape means saved files still load after a module changes.
/// </summary>
[JsonConverter(typeof(ProtocolOptionsJsonConverter))]
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Named for what it holds; the dictionary interface is a convenience.")]
public sealed class ProtocolOptions : IReadOnlyDictionary<string, string>, IEquatable<ProtocolOptions>
{
	private readonly ImmutableSortedDictionary<string, string> _values;

	private ProtocolOptions(ImmutableSortedDictionary<string, string> values) => _values = values;

	public static ProtocolOptions Empty { get; } = new(ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal));

	public int Count => _values.Count;

	public IEnumerable<string> Keys => _values.Keys;

	public IEnumerable<string> Values => _values.Values;

	public string this[string key] => _values[key];

	public static ProtocolOptions From(IEnumerable<KeyValuePair<string, string>>? values) =>
		values is null ? Empty : new(ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, values));

	public bool ContainsKey(string key) => _values.ContainsKey(key);

	public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);

	public string? GetString(string key) => _values.TryGetValue(key, out string? value) ? value : null;

	public string GetString(string key, string fallback) => GetString(key) ?? fallback;

	public int GetInt32(string key, int fallback) =>
		_values.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
			? parsed
			: fallback;

	public bool GetBoolean(string key, bool fallback) =>
		_values.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed) ? parsed : fallback;

	/// <summary>
	/// The enum stored at <paramref name="key"/>, or <paramref name="fallback"/> for anything else. Only text that names
	/// one defined member counts: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also takes numbers and
	/// comma separated lists, and either can land on a value nobody stored, since a list is ORed together
	/// (<c>"Http, Socks4"</c> is the value of <c>Socks5</c>). Everything here was written by <see cref="WithEnum"/>, so
	/// nothing is lost by refusing the rest.
	/// </summary>
	public TEnum GetEnum<TEnum>(string key, TEnum fallback) where TEnum : struct, Enum
	{
		string value = _values.TryGetValue(key, out string? stored) ? stored.Trim() : "";
		return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
			&& Enum.IsDefined(parsed)
			&& string.Equals(parsed.ToString(), value, StringComparison.OrdinalIgnoreCase)
				? parsed
				: fallback;
	}

	/// <summary>Returns a copy with the key set, or removed when <paramref name="value"/> is null or empty.</summary>
	public ProtocolOptions With(string key, string? value) =>
		string.IsNullOrEmpty(value) ? new(_values.Remove(key)) : new(_values.SetItem(key, value));

	public ProtocolOptions With(string key, int? value) =>
		With(key, value?.ToString(CultureInfo.InvariantCulture));

	public ProtocolOptions With(string key, bool? value) =>
		With(key, value is null ? null : value.Value ? "true" : "false");

	public ProtocolOptions WithEnum<TEnum>(string key, TEnum? value) where TEnum : struct, Enum =>
		With(key, value?.ToString());

	public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public bool Equals(ProtocolOptions? other)
	{
		if (other is null)
		{
			return false;
		}

		if (ReferenceEquals(this, other))
		{
			return true;
		}

		return Count == other.Count && _values.All(pair => other.TryGetValue(pair.Key, out string value) && value == pair.Value);
	}

	public override bool Equals(object? obj) => Equals(obj as ProtocolOptions);

	public override int GetHashCode()
	{
		HashCode hash = default;
		foreach (KeyValuePair<string, string> pair in _values)
		{
			hash.Add(pair.Key);
			hash.Add(pair.Value);
		}

		return hash.ToHashCode();
	}
}
