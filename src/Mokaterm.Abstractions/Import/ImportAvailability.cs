namespace Mokaterm.Abstractions.Import;

/// <summary>Whether a source had anything to offer. A source never throws to say "nothing here".</summary>
public enum ImportAvailability
{
	/// <summary>The source was read and produced at least one entry.</summary>
	Found,

	/// <summary>Nothing is installed, or the file holds no sessions.</summary>
	NotFound,

	/// <summary>The location exists but could not be read: no permission, wrong format, damaged file.</summary>
	Unreadable,
}
