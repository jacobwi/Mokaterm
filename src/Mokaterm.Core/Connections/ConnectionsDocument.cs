using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Core.Connections;

/// <summary>The encrypted <c>connections</c> vault document.</summary>
internal sealed record ConnectionsDocument
{
	public const string DocumentName = "connections";

	public int Version { get; init; } = 1;

	public IReadOnlyList<ConnectionFolder> Folders { get; init; } = [];

	public IReadOnlyList<HostProfile> Hosts { get; init; } = [];

	public IReadOnlyList<ConnectionProfile> Connections { get; init; } = [];
}
