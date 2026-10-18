using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Abstractions.Import;

/// <summary>
/// One host and login an import source found. Secrets are never part of this: sources read addresses, users and
/// options only, and a key file is recorded as a path, not as key material. On the desktop the importer can copy that
/// file into the keychain when the import runs (see <see cref="KeyFile"/>).
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
	/// Private key file the other client pointed at, as it wrote it. Sources never open it. The desktop importer can
	/// copy it into the keychain; otherwise, and always on the web host, the path goes into the host's notes.
	/// </summary>
	public string? IdentityFilePath { get; init; }

	/// <summary>How <see cref="IdentityFilePath"/> is written, which decides how it turns into a file on this machine.</summary>
	public IdentityPathStyle IdentityPathStyle { get; init; }

	/// <summary>
	/// What adding <see cref="IdentityFilePath"/> to the keychain would do, filled in by the importer's preview where
	/// keys can be imported. Null when there is no key file, or on a host that never reads one.
	/// </summary>
	public ImportedKeyFile? KeyFile { get; init; }

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
