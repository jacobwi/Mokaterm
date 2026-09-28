using System.Buffers;
using System.Net;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.DevHost.Demo.Terminal;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// Connects to nothing: walks through the usual connect steps with short delays, asks the real host verifier about a
/// fixed host key and hands back an in-memory session.
/// </summary>
internal sealed class DemoProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "demo";

	private const int MaxUserNameLength = 32;

	// Fixed, so accepting and saving the key once keeps every later connect to the same host and port quiet.
	private const string HostKeyFingerprint = "SHA256:sCz7Y4qSG16iVzAPTZW58zf3p9SFpfxO5hfbpXWqff8";

	private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(250);

	private static readonly SearchValues<char> UserNameChars =
		SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-");

	private readonly TimeProvider _timeProvider;
	private readonly ILogger<DemoTerminalChannel> _logger;

	public DemoProtocolProvider(TimeProvider timeProvider, ILogger<DemoTerminalChannel> logger)
	{
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "Demo",
		Description = "In-memory shell and file system for UI work",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.FileSystem | ProtocolCapabilities.Elevation,
		AuthenticationMethods = [AuthenticationMethod.Anonymous, AuthenticationMethod.Password],
		RequiresUsername = true,
		Order = 100,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		context.Status?.Report("Resolving host");
		await Task.Delay(StepDelay, _timeProvider, cancellationToken);
		context.Status?.Report("Negotiating keys");
		await Task.Delay(StepDelay, _timeProvider, cancellationToken);

		HostIdentity identity = new()
		{
			Host = context.Host.Address,
			Port = context.Port,
			Kind = HostIdentityKind.SshHostKey,
			Algorithm = "ssh-ed25519",
			Fingerprint = HostKeyFingerprint,
		};

		if (!await context.HostVerifier.VerifyAsync(identity, cancellationToken))
		{
			throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, "The host key of the demo server was not trusted.");
		}

		context.Status?.Report("Authenticating");
		await Task.Delay(StepDelay, _timeProvider, cancellationToken);

		string userName;
		using (LoginCredentials credentials = await context.Credentials.GetAsync(cancellationToken)
			?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled."))
		{
			// Any password works; only the name matters, because it picks the home directory and the prompt.
			userName = credentials.Username;
		}

		if (!IsValidUserName(userName))
		{
			throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				$"The demo server has no user named '{userName}'. Use letters, digits, '.', '_' or '-'.");
		}

		return new DemoSession(DemoAccount.ForUser(userName), HostNameOf(context.Host), context.Host.Address, context.TerminalSize, _timeProvider, _logger);
	}

	private static bool IsValidUserName(string userName) =>
		userName.Length is > 0 and <= MaxUserNameLength
		&& userName[0] != '-'
		&& !userName.AsSpan().ContainsAnyExcept(UserNameChars);

	// A prompt shows a host name rather than an address: the saved name when it makes one, else the first DNS label, else
	// a cloud-style name built from the IP address.
	private static string HostNameOf(HostProfile host)
	{
		string fromName = Slug(host.Name);
		if (fromName.Length > 0)
		{
			return fromName;
		}

		if (IPAddress.TryParse(host.Address, out _))
		{
			return "ip-" + Slug(host.Address);
		}

		string fromAddress = Slug(host.Address.Split('.')[0]);
		return fromAddress.Length > 0 ? fromAddress : "demo";
	}

	private static string Slug(string text)
	{
		string slug = string.Create(text.Length, text, static (span, source) =>
		{
			for (int i = 0; i < source.Length; i++)
			{
				span[i] = char.IsAsciiLetterOrDigit(source[i]) ? char.ToLowerInvariant(source[i]) : '-';
			}
		});

		return slug.Trim('-');
	}
}
