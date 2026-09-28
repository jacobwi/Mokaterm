namespace Mokaterm.Core.Import;

/// <summary>The file an export writes and <see cref="ConnectionBundleImportSource"/> reads back.</summary>
internal sealed record ConnectionBundle
{
	/// <summary>Names the format, so a picked file that happens to be JSON is refused with something readable.</summary>
	public const string BundleKind = "mokaterm-connections";

	public const int CurrentVersion = 1;

	public string Kind { get; init; } = BundleKind;

	public int Version { get; init; } = CurrentVersion;

	public DateTimeOffset ExportedAt { get; init; }

	public IReadOnlyList<ConnectionBundleEntry> Entries { get; init; } = [];
}
