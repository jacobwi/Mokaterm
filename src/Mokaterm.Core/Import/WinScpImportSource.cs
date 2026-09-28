using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads WinSCP sessions from <c>HKCU\Software\Martin Prikryl\WinSCP 2\Sessions</c> or from a portable
/// <c>WinSCP.ini</c>. Saved passwords are deliberately left behind: WinSCP stores them so they can be turned back
/// into plain text, and copying them would only spread them further.
/// </summary>
internal sealed class WinScpImportSource : IConnectionImportSource
{
	public const string SourceId = "winscp";

	internal const string RegistryPath = @"Software\Martin Prikryl\WinSCP 2\Sessions";

	private const string RegistryMarker = @"WinSCP 2\Sessions\";

	private const string IniMarker = @"Sessions\";

	private const string DefaultSession = "Default Settings";

	private const int MaxFileBytes = 16 * 1024 * 1024;

	// WinSCP's TFSProtocol: 0 SCP, 1 SFTP, 2 SFTP only, 5 FTP, 6 WebDAV, 7 S3.
	private const int ProtocolScp = 0;
	private const int ProtocolSftp = 1;
	private const int ProtocolSftpOnly = 2;
	private const int ProtocolFtp = 5;
	private const int ProtocolWebDav = 6;
	private const int ProtocolS3 = 7;

	// WinSCP's TFtps: 0 none, 1 implicit, 2 explicit over SSL, 3 explicit over TLS.
	private const int FtpsImplicit = 1;

	private readonly Func<IReadOnlyList<ImportSection>> _readRegistry;

	public WinScpImportSource()
		: this(() => WindowsRegistrySessions.ReadSubKeys(RegistryPath))
	{
	}

	internal WinScpImportSource(Func<IReadOnlyList<ImportSection>> readRegistry) => _readRegistry = readRegistry;

	public ImportSourceInfo Info { get; } = new()
	{
		Id = SourceId,
		DisplayName = "WinSCP",
		Description = "Saved sites in the registry",
		Kind = ImportSourceKind.Installed,
		AcceptsFile = true,
		FileExtensions = [".ini"],
	};

	public ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		if (request.File is { } file)
		{
			return ValueTask.FromResult(file.Content.Length > MaxFileBytes
				? ImportPreview.Failed(SourceId, "This file is too large to be a WinSCP.ini.", file.Path ?? file.Name)
				: Build(IniFileReader.Parse(file.Content), IniMarker, file.Path ?? file.Name, "This file has no WinSCP sites in it."));
		}

		if (!WindowsRegistrySessions.IsSupported)
		{
			return ValueTask.FromResult(ImportPreview.Nothing(SourceId, "WinSCP keeps its sites in the Windows registry. Pick a WinSCP.ini instead."));
		}

		return ValueTask.FromResult(Build(
			_readRegistry(),
			RegistryMarker,
			@"HKEY_CURRENT_USER\" + RegistryPath,
			"No WinSCP sites are saved on this machine."));
	}

	internal static ImportPreview Build(IReadOnlyList<ImportSection> sections, string marker, string location, string emptyMessage)
	{
		List<ImportedEntry> entries = [];
		List<ImportSkip> skipped = [];
		foreach (SessionValues session in SessionSections.Under(sections, marker))
		{
			if (session.Name.Equals(DefaultSession, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (Map(session, out ImportedEntry? entry, out ImportSkip? skip))
			{
				entries.Add(entry!);
			}
			else if (skip is not null)
			{
				skipped.Add(skip);
			}
		}

		return entries.Count == 0
			? ImportPreview.Nothing(SourceId, emptyMessage, location) with { Skipped = skipped }
			: new ImportPreview
			{
				SourceId = SourceId,
				Availability = ImportAvailability.Found,
				Location = location,
				Entries = entries,
				Skipped = skipped,
			};
	}

	private static bool Map(SessionValues session, out ImportedEntry? entry, out ImportSkip? skip)
	{
		entry = null;
		skip = null;

		int protocol = session.Number("FSProtocol") ?? ProtocolSftp;
		string? protocolId = protocol switch
		{
			ProtocolScp or ProtocolSftp or ProtocolSftpOnly => ImportProtocols.Sftp,
			ProtocolFtp => ImportProtocols.Ftp,
			_ => null,
		};

		if (protocolId is null)
		{
			skip = new ImportSkip(session.Name, protocol switch
			{
				ProtocolWebDav => "WebDAV is not supported.",
				ProtocolS3 => "Amazon S3 is not supported.",
				_ => "The site uses a protocol Mokaterm does not have.",
			});
			return false;
		}

		if (session.Text("HostName") is not { Length: > 0 } address)
		{
			skip = new ImportSkip(session.Name, "The site has no host name.");
			return false;
		}

		bool ftp = protocolId == ImportProtocols.Ftp;
		int ftps = session.Number("Ftps") ?? 0;
		ProtocolOptions options = ProtocolOptions.Empty;
		if (ftp && ftps > 0)
		{
			options = options.With(
				ImportProtocols.FtpEncryptionKey,
				ftps == FtpsImplicit ? ImportProtocols.FtpEncryptionImplicit : ImportProtocols.FtpEncryptionExplicit);
		}

		// Implicit FTPS answers on 990, and the descriptor's default port is 21, so it has to be written out.
		int defaultPort = ftp ? (ftps == FtpsImplicit ? -1 : 21) : 22;
		(string? folder, string name) = SplitFolder(session.Name);
		entry = new ImportedEntry
		{
			Key = SourceId + ":" + session.Name,
			Name = name,
			Address = address,
			ProtocolId = protocolId,
			Port = ImportPorts.Normalize(session.Number("PortNumber"), defaultPort),
			Username = session.Text("UserName"),
			Options = options,
			FolderPath = folder,
			Notes = protocol == ProtocolScp ? "WinSCP used SCP for this site; Mokaterm connects over SFTP." : null,
		};

		return true;
	}

	/// <summary>WinSCP nests its sites with slashes in the name, so <c>work/prod</c> is prod inside work.</summary>
	private static (string? Folder, string Name) SplitFolder(string sessionName)
	{
		int separator = sessionName.LastIndexOf('/');
		if (separator <= 0 || separator == sessionName.Length - 1)
		{
			return (null, sessionName);
		}

		return (sessionName[..separator], sessionName[(separator + 1)..]);
	}
}
