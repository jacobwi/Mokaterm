using System.Security.Cryptography.X509Certificates;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

internal sealed record LoopbackFtpUser(string Password, string Home);

internal sealed class LoopbackFtpServerOptions
{
	public FakeFileTree Files { get; } = new();

	public Dictionary<string, LoopbackFtpUser> Users { get; } = new(StringComparer.Ordinal);

	/// <summary>Accepts "anonymous" with any password and starts it in <see cref="AnonymousHome"/>.</summary>
	public bool AllowAnonymous { get; init; }

	public string AnonymousHome { get; init; } = "/pub";

	/// <summary>Advertises MLST and answers MLSD and MLST.</summary>
	public bool MachineListings { get; init; }

	/// <summary>When false, LIST takes "-a" as part of the path, like servers without ls options.</summary>
	public bool ListAllFilesSupported { get; init; } = true;

	public bool SupportsChmod { get; init; } = true;

	public bool SupportsMfmt { get; init; }

	/// <summary>Control connections allowed at once; the next one is greeted with 421.</summary>
	public int MaxConnections { get; init; } = int.MaxValue;

	/// <summary>Enables AUTH TLS, or TLS on connect when <see cref="ImplicitTls"/> is set.</summary>
	public X509Certificate2? Certificate { get; init; }

	public bool ImplicitTls { get; init; }

	/// <summary>
	/// The address a PASV reply names, as its four comma separated numbers. The data listener stays on 127.0.0.1, so
	/// only a client that ignores the address finds it.
	/// </summary>
	public string PassiveAddress { get; init; } = "127,0,0,1";

	/// <summary>STOR opens the data connection and then never reads from it, like a server whose disk hung.</summary>
	public bool StallUploads { get; init; }

	/// <summary>RETR sends half the file and then nothing, with the data connection left open.</summary>
	public bool StallDownloads { get; init; }

	/// <summary>QUIT gets no reply and the connection stays open, like a server that stopped answering.</summary>
	public bool IgnoreQuit { get; init; }
}
