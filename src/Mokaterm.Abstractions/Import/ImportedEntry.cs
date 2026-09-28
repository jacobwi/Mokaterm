using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Abstractions.Import;

/// <summary>
/// One host and login an import source found. Secrets are never part of this: sources read addresses, users and
/// options only, and a key file is recorded as a path, not as key material.
/// </summary>
public sealed record ImportedEntry
{
	/// <summary>Stable inside one preview, so the wizard can track which entries are ticked.</summary>
	public required string Key { get; init; }

	/// <summary>The session name in the other client, shown in the preview and used as the host name.</summary>
	public required string Name { get; init; }

	/// <summary>Hostname or IP address.</summary>
	public required string Address { get; init; }

	public required string ProtocolId { get; init; }

	/// <summary>Null means the protocol's default port.</summary>
	public int? Port { get; init; }

	public string? Username { get; init; }

	public AuthenticationMethod AuthenticationMethod { get; init; }

	/// <summary>Protocol options the source could map, such as the FTPS encryption mode.</summary>
	public ProtocolOptions Options { get; init; } = ProtocolOptions.Empty;

	/// <summary>
	/// Private key file the other client pointed at. Recorded so the user can attach the key later; the file is
	/// never opened and never imported.
	/// </summary>
	public string? IdentityFilePath { get; init; }

	/// <summary>Folders to create under the chosen destination, '/' separated. Null keeps the entry at the destination.</summary>
	public string? FolderPath { get; init; }

	/// <summary>Kept as the host's notes, for settings that have no home yet such as a jump host.</summary>
	public string? Notes { get; init; }

	/// <summary>Tags for the host. Only an export of our own carries these; other clients have nothing like them.</summary>
	public IReadOnlyList<string> Tags { get; init; } = [];

	public HostEnvironment Environment { get; init; }

	/// <summary>The host's own colour as <c>#rrggbb</c>, or null to let the address pick one.</summary>
	public string? Color { get; init; }

	/// <summary>A name for the login itself, shown instead of user@host.</summary>
	public string? Label { get; init; }

	/// <summary>Set by the importer when a saved login already has this address, user and protocol.</summary>
	public bool AlreadyExists { get; init; }
}
