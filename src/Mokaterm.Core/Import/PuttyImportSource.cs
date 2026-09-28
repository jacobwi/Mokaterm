using Mokaterm.Abstractions.Import;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads PuTTY sessions from <c>HKCU\Software\SimonTatham\PuTTY\Sessions</c> or from a <c>.reg</c> export, which
/// is how most people move their sessions between machines. PuTTY never stores passwords, so there are none to skip.
/// </summary>
internal sealed class PuttyImportSource : IConnectionImportSource
{
	public const string SourceId = "putty";

	internal const string RegistryPath = @"Software\SimonTatham\PuTTY\Sessions";

	private const string Marker = @"SimonTatham\PuTTY\Sessions\";

	/// <summary>The template PuTTY writes for new sessions, not a host.</summary>
	private const string DefaultSession = "Default Settings";

	private const int MaxFileBytes = 8 * 1024 * 1024;

	private readonly Func<IReadOnlyList<ImportSection>> _readRegistry;

	public PuttyImportSource()
		: this(() => WindowsRegistrySessions.ReadSubKeys(RegistryPath))
	{
	}

	internal PuttyImportSource(Func<IReadOnlyList<ImportSection>> readRegistry) => _readRegistry = readRegistry;

	public ImportSourceInfo Info { get; } = new()
	{
		Id = SourceId,
		DisplayName = "PuTTY",
		Description = "Saved sessions in the registry",
		Kind = ImportSourceKind.Installed,
		AcceptsFile = true,
		FileExtensions = [".reg"],
	};

	public ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		if (request.File is { } file)
		{
			return ValueTask.FromResult(file.Content.Length > MaxFileBytes
				? ImportPreview.Failed(SourceId, "This file is too large to be a registry export.", file.Path ?? file.Name)
				: Build(RegFileReader.Parse(file.Content), file.Path ?? file.Name, "This export has no PuTTY sessions in it."));
		}

		if (!WindowsRegistrySessions.IsSupported)
		{
			return ValueTask.FromResult(ImportPreview.Nothing(SourceId, "PuTTY keeps its sessions in the Windows registry. Pick a .reg export instead."));
		}

		return ValueTask.FromResult(Build(
			_readRegistry(),
			@"HKEY_CURRENT_USER\" + RegistryPath,
			"No PuTTY sessions are saved on this machine."));
	}

	internal static ImportPreview Build(IReadOnlyList<ImportSection> sections, string location, string emptyMessage)
	{
		List<ImportedEntry> entries = [];
		List<ImportSkip> skipped = [];
		foreach (SessionValues session in SessionSections.Under(sections, Marker))
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

		string protocol = session.Text("Protocol") ?? "ssh";
		string? protocolId = protocol.ToLowerInvariant() switch
		{
			"ssh" => ImportProtocols.Ssh,
			"telnet" => ImportProtocols.Telnet,
			_ => null,
		};

		if (protocolId is null)
		{
			skip = new ImportSkip(session.Name, $"PuTTY's {protocol} protocol has no equivalent in Mokaterm.");
			return false;
		}

		if (session.Text("HostName") is not { Length: > 0 } address)
		{
			skip = new ImportSkip(session.Name, "The session has no host name.");
			return false;
		}

		int defaultPort = protocolId == ImportProtocols.Telnet ? 23 : 22;
		entry = new ImportedEntry
		{
			Key = SourceId + ":" + session.Name,
			Name = session.Name,
			Address = address,
			ProtocolId = protocolId,
			Port = ImportPorts.Normalize(session.Number("PortNumber"), defaultPort),
			Username = session.Text("UserName"),

			// PuTTY points at a .ppk file. The path is recorded so the user can attach the key; it is never opened.
			IdentityFilePath = session.Text("PublicKeyFile"),
		};

		return true;
	}
}
