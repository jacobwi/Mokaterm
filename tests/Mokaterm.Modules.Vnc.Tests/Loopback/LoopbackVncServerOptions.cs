using System.Security.Cryptography.X509Certificates;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Tests.Loopback;

/// <summary>How the loopback server behaves during the handshake.</summary>
internal sealed record LoopbackVncServerOptions
{
	public string Greeting { get; init; } = "RFB 003.008\n";

	/// <summary>Types offered with RFB 3.7 and later, or the single type dictated with 3.3.</summary>
	public IReadOnlyList<int> SecurityTypes { get; init; } = [(int)VncSecurityType.None];

	public IReadOnlyList<int> VeNCryptSubtypes { get; init; } = [];

	/// <summary>VeNCrypt version the server announces.</summary>
	public (byte Major, byte Minor) VeNCryptVersion { get; init; } = (0, 2);

	/// <summary>The password VNC authentication and the Plain subtypes accept.</summary>
	public string? Password { get; init; }

	public string? Username { get; init; }

	/// <summary>Server certificate for the X509 subtypes.</summary>
	public X509Certificate2? Certificate { get; init; }

	public int Width { get; init; } = 800;

	public int Height { get; init; } = 600;

	public string DesktopName { get; init; } = "loopback :1";

	/// <summary>Answers the security negotiation with no types and this reason.</summary>
	public string? RefuseReason { get; init; }

	/// <summary>Sends this security result instead of the one the password check produced.</summary>
	public uint? ForcedSecurityResult { get; init; }

	/// <summary>Closes the connection right after the greeting, like a server that is full.</summary>
	public bool CloseAfterGreeting { get; init; }

	/// <summary>From this connection on (the first is 1), the server accepts and then says nothing at all.</summary>
	public int? SilentFromAttempt { get; init; }
}
