using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>
/// An owner or group being edited for one or more entries: the name they share, if any, and what the field holds now. An
/// empty field keeps each entry's own name.
/// </summary>
internal sealed class AccountField
{
	/// <param name="names">The name of each entry, null where the server did not report one.</param>
	public AccountField(IEnumerable<string?> names)
	{
		bool first = true;
		string? shared = null;
		foreach (string? name in names)
		{
			if (first)
			{
				shared = name;
				first = false;
			}
			else if (!string.Equals(name, shared, StringComparison.Ordinal))
			{
				IsMixed = true;
			}
		}

		Initial = IsMixed ? null : shared;
		Text = Initial ?? "";
	}

	/// <summary>The name every entry has, or null when they differ or none was reported.</summary>
	public string? Initial { get; }

	/// <summary>The entries have different names.</summary>
	public bool IsMixed { get; }

	public string Text { get; set; }

	public string Trimmed => Text.Trim();

	public bool IsEmpty => Trimmed.Length == 0;

	public bool IsValid => IsEmpty || RemoteAccount.IsValidName(Trimmed);

	/// <summary>The name in the field when it is valid and differs from <see cref="Initial"/>.</summary>
	public string? Changed => !IsEmpty && IsValid && !string.Equals(Trimmed, Initial, StringComparison.Ordinal) ? Trimmed : null;

	/// <summary>
	/// The name to set: <see cref="Changed"/>, or with <paramref name="includeUnchanged"/> any valid name in the field, for
	/// giving everything inside a folder the name the folder already has.
	/// </summary>
	public string? ToApply(bool includeUnchanged) => includeUnchanged && !IsEmpty && IsValid ? Trimmed : Changed;
}
