using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Security;

/// <summary>The encrypted <c>known-hosts</c> vault document.</summary>
internal sealed record KnownHostsDocument
{
	public const string DocumentName = "known-hosts";

	public int Version { get; init; } = 1;

	public IReadOnlyList<KnownHost> Hosts { get; init; } = [];
}
