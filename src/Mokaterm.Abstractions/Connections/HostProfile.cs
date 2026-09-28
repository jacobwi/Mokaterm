namespace Mokaterm.Abstractions.Connections;

/// <summary>
/// A machine you connect to. One host carries any number of <see cref="ConnectionProfile"/> logins,
/// such as <c>abc@10.10.2.3</c> over SSH and <c>bcd@10.10.2.3</c> over FTP.
/// </summary>
public sealed record HostProfile
{
	public required Guid Id { get; init; }

	/// <summary>Label shown in the tree. Falls back to <see cref="Address"/> when blank.</summary>
	public string Name { get; init; } = "";

	/// <summary>Hostname, IPv4 or IPv6 address.</summary>
	public required string Address { get; init; }

	public Guid? FolderId { get; init; }

	public IReadOnlyList<string> Tags { get; init; } = [];

	public HostEnvironment Environment { get; init; }

	/// <summary>Optional <c>#rrggbb</c> accent for tabs and tree markers.</summary>
	public string? Color { get; init; }

	public string? Notes { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset UpdatedAt { get; init; }

	public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name;
}
