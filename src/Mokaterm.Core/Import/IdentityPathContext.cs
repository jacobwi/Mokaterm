namespace Mokaterm.Core.Import;

/// <summary>What a key path can refer to: this machine and its account, and the login the path belongs to.</summary>
internal sealed record IdentityPathContext
{
	public required string HomeDirectory { get; init; }

	/// <summary>This machine's account name, for <c>%u</c> and for <c>%r</c> when the login names no user.</summary>
	public required string LocalUser { get; init; }

	/// <summary>This machine's name, for <c>%l</c> and <c>%L</c>.</summary>
	public required string LocalHost { get; init; }

	/// <summary>Reads an environment variable, null when it is not set.</summary>
	public required Func<string, string?> Variable { get; init; }

	/// <summary>The host as the config names it, for <c>%n</c>.</summary>
	public required string Alias { get; init; }

	/// <summary>The address the login connects to, for <c>%h</c>.</summary>
	public required string HostName { get; init; }

	public string? RemoteUser { get; init; }

	public int? Port { get; init; }
}
