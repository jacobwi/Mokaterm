using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>Everything the quick connect list is picked from, read when the box takes focus.</summary>
/// <remarks>A class and not a record: the catalog and the history name machines, which a record would print.</remarks>
internal sealed class QuickConnectSources
{
	public static QuickConnectSources Empty { get; } = new();

	public ConnectionCatalog Catalog { get; init; } = ConnectionCatalog.Empty;

	/// <summary>Newest first.</summary>
	public IReadOnlyList<QuickConnectRecent> Recents { get; init; } = [];

	/// <summary>The protocol <see cref="Detected"/> came from, or null when it offered nothing.</summary>
	public string? DetectedProtocolId { get; init; }

	public IReadOnlyList<QuickConnectSuggestion> Detected { get; init; } = [];

	/// <summary>Descriptors by id for the protocols this host can open; a login or entry for any other is left out.</summary>
	public IReadOnlyDictionary<string, ProtocolDescriptor> Protocols { get; init; } =
		new Dictionary<string, ProtocolDescriptor>(StringComparer.OrdinalIgnoreCase);
}
