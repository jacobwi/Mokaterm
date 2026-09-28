namespace Mokaterm.Abstractions.Import;

/// <summary>The file an export produced, plus what went into it.</summary>
public sealed record ConnectionExport
{
	public required string FileName { get; init; }

	public required byte[] Content { get; init; }

	public int Folders { get; init; }

	public int Hosts { get; init; }

	public int Logins { get; init; }

	/// <summary>Hosts left out because they have no login yet; there would be nothing to connect with.</summary>
	public int HostsWithoutLogin { get; init; }
}
