using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>What reading a security key file found. <see cref="Key"/> is set exactly when the status is valid.</summary>
public sealed record SecurityKeyFileResult(PrivateKeyStatus Status, SshSecurityKey? Key = null, string? Error = null)
{
	[MemberNotNullWhen(true, nameof(Key))]
	public bool IsValid => Status == PrivateKeyStatus.Valid && Key is not null;

	public static SecurityKeyFileResult Valid(SshSecurityKey key) => new(PrivateKeyStatus.Valid, key);

	public static SecurityKeyFileResult Failed(PrivateKeyStatus status, string error) => new(status, null, error);
}
