using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Core.Import;

/// <summary>One host and login in an export. It carries no secret, only what the connection editor shows.</summary>
internal sealed record ConnectionBundleEntry
{
	public required string Name { get; init; }

	public required string Address { get; init; }

	public required string ProtocolId { get; init; }

	public int? Port { get; init; }

	public string? Username { get; init; }

	public AuthenticationMethod AuthenticationMethod { get; init; }

	public ProtocolOptions Options { get; init; } = ProtocolOptions.Empty;

	/// <summary>Folders from the top of the tree down to the host, '/' separated.</summary>
	public string? FolderPath { get; init; }

	public string? Notes { get; init; }

	public IReadOnlyList<string> Tags { get; init; } = [];

	public HostEnvironment Environment { get; init; }

	public string? Color { get; init; }

	public string? Label { get; init; }
}
