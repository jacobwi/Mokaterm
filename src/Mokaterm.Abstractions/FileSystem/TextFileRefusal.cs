namespace Mokaterm.Abstractions.FileSystem;

/// <summary>Why a remote file cannot be read as text.</summary>
public enum TextFileRefusal
{
	None,

	/// <summary>Bigger than the cap, so opening it would be a piece of a file that saving would truncate.</summary>
	TooLarge,

	/// <summary>Holds a NUL, so it is not text and an editor would corrupt it.</summary>
	Binary,

	/// <summary>Not valid UTF-8. Decoding with replacement characters would write those characters back.</summary>
	NotUtf8,
}
