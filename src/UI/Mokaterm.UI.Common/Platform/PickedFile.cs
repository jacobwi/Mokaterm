using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// What <see cref="LocalFilePick"/> came back with: a file read whole, a message for the user, or neither, which is the
/// user closing the picker.
/// </summary>
/// <typeparam name="TContent">The bytes or the text the caller asked for.</typeparam>
public sealed record PickedFile<TContent>
	where TContent : class
{
	/// <summary>The picked file's name. Null unless it was read.</summary>
	public string? Name { get; init; }

	/// <summary>Everything the file held. Null unless it was read.</summary>
	public TContent? Content { get; init; }

	/// <summary>Why nothing was read, for the user. Null both when the picker was closed and when the read worked.</summary>
	public string? Error { get; init; }

	[MemberNotNullWhen(true, nameof(Name), nameof(Content))]
	public bool WasRead => Name is not null && Content is not null;

	/// <summary>The one wording for a file over a caller's cap.</summary>
	internal static PickedFile<TContent> TooLarge(string what) => new() { Error = $"That file is too large to be {what}." };
}
