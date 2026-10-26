using System.Globalization;
using System.Net.Sockets;
using Devolutions.IronRdp;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Sessions;

namespace Mokaterm.RdpProbe;

/// <summary>
/// One RDP connect with the module's own connector, every step printed with its time. Exit code 0 means connected
/// (and, unless --connect-only, a first picture arrived), 1 a failed connect, 2 a command line it could not read.
/// </summary>
internal static class Program
{
	private static readonly TimeSpan PictureWait = TimeSpan.FromSeconds(30);

	public static async Task<int> Main(string[] args)
	{
		ProbeArguments? arguments = ProbeArguments.Parse(args, out string? usageError);
		if (arguments is null)
		{
			if (usageError is not null)
			{
				Console.Error.WriteLine(usageError);
				Console.Error.WriteLine();
			}

			Console.Error.WriteLine(ProbeArguments.Usage);
			return usageError is null ? 0 : 2;
		}

		PrintHeader(arguments);
		using LoginCredentials credentials = ReadLogin(arguments);
		ProbeOutput output = new();
		RdpConnector connector = new(
			arguments.Host,
			arguments.Port,
			RdpConnectionOptions.Default with
			{
				Domain = arguments.Domain ?? "",
				NetworkLevelAuthentication = arguments.NetworkLevelAuthentication,
				Autologon = arguments.Autologon,
			},
			TimeSpan.FromSeconds(arguments.TimeoutSeconds),
			new ConsoleCertificateCheck(output, arguments.AcceptCertificate))
		{
			Trace = new TraceLines(output),
		};

		using CancellationTokenSource stop = new();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			stop.Cancel();
		};

		RdpTransport transport;
		try
		{
			transport = await connector.ConnectAsync(credentials, output, stop.Token);
		}
		catch (Exception ex)
		{
			Console.WriteLine();
			Console.WriteLine(string.Create(
				CultureInfo.InvariantCulture,
				$"FAILED after {output.Elapsed.TotalSeconds:0.0} s, in the step: {output.LastStep ?? "before connecting"}"));
			PrintRoute(connector);
			PrintFailure(ex);
			return 1;
		}

		RdpConnectionInfo info = transport.Describe();
		output.Event(string.Create(CultureInfo.InvariantCulture, $"Connected: {info.Width} x {info.Height}, {info.Security}"));
		PrintRoute(connector);
		if (arguments.ConnectOnly)
		{
			await transport.DisposeAsync();
			return 0;
		}

		return await WaitForPictureAsync(transport, output, TimeSpan.FromSeconds(arguments.SignInWaitSeconds), stop.Token);
	}

	/// <summary>The security protocol the server picked and how the login went to it, for the attempt just made.</summary>
	private static void PrintRoute(RdpConnector connector)
	{
		RdpSecurityProtocols? picked = connector.LastNegotiated;
		Console.WriteLine("Negotiated:  " + (picked is { } protocol
			? RdpNegotiation.Name(protocol) + " " + DescribeProtocol(protocol)
			: "none (no answer to the RDP request that picked a protocol this client asked for)"));
		Console.WriteLine("Sign-in:     " + connector.LastRoute switch
		{
			RdpSignInRoute.NetworkLevelAuthentication when picked == RdpSecurityProtocols.Ssl =>
				"network level authentication was asked for, but the server picked TLS alone, so the name and password go in the Client Info PDU, flagged INFO_AUTOLOGON",
			RdpSignInRoute.NetworkLevelAuthentication => "network level authentication (CredSSP)",
			RdpSignInRoute.Automatic => "the name and password in the Client Info PDU, flagged INFO_AUTOLOGON",
			RdpSignInRoute.SignInScreen => "no password; the server's own sign-in screen asks",
			_ => "not decided",
		});
	}

	private static string DescribeProtocol(RdpSecurityProtocols protocol) => protocol switch
	{
		RdpSecurityProtocols.Ssl => "(TLS, the login checked by Windows' own sign-in)",
		RdpSecurityProtocols.Hybrid => "(TLS and CredSSP)",
		RdpSecurityProtocols.HybridEx => "(TLS and CredSSP, with the Early User Authorization Result)",
		_ => "",
	};

	private static void PrintHeader(ProbeArguments arguments)
	{
		string version = typeof(Connection).Assembly.GetName().Version?.ToString() ?? "unknown";
		Console.WriteLine("Mokaterm RDP probe, IronRDP " + version + ", " + Environment.OSVersion.VersionString);
		bool login = arguments.User is not null;
		string who = login ? "user " + arguments.User : "no login";
		string domain = string.IsNullOrEmpty(arguments.Domain) ? "" : ", domain " + arguments.Domain;
		string nla = !login ? "off"
			: arguments.NetworkLevelAuthentication ? "on"
			: arguments.Autologon ? "off, signing in with the Client Info PDU"
			: "off, stopping at the server's sign-in screen";
		Console.WriteLine(string.Create(
			CultureInfo.InvariantCulture,
			$"Target {HostEndpoint.Format(arguments.Host, arguments.Port)}, {who}{domain}, network level authentication {nla}, timeout {arguments.TimeoutSeconds} s"));
	}

	private static LoginCredentials ReadLogin(ProbeArguments arguments)
	{
		if (arguments.User is not { } user)
		{
			return new LoginCredentials { Username = "", Method = AuthenticationMethod.Anonymous };
		}

		return new LoginCredentials
		{
			Username = user,
			Method = AuthenticationMethod.Password,

			// A password that will not be sent is not asked for.
			Password = arguments.SendsPassword ? PasswordPrompt.Read(user) : null,
		};
	}

	/// <summary>
	/// Runs the session the way the app's view does, waits for the first region the server draws, and then for what
	/// Windows says about signing in.
	/// </summary>
	private static async Task<int> WaitForPictureAsync(RdpTransport transport, ProbeOutput output, TimeSpan signInWait, CancellationToken cancellationToken)
	{
		await using RdpSession session = new(transport, new RdpSettings(), null, NullLogger.Instance);
		SignInWatch signIn = new(output);
		session.LogonNoticed += signIn.Notice;
		signIn.Catch(session.Logon);
		FirstPictureSink sink = new();
		await using IRdpChannel channel = await session.AttachAsync(sink, cancellationToken);
		await channel.StartAsync(cancellationToken);
		Task finished = await Task.WhenAny(sink.FirstFrame, session.Completion, Task.Delay(PictureWait, cancellationToken));
		if (finished == sink.FirstFrame)
		{
			int bytes = await sink.FirstFrame;
			output.Event(string.Create(CultureInfo.InvariantCulture, $"First picture: {bytes} bytes."));
			await signIn.WaitAsync(session.Completion, signInWait, cancellationToken);
			output.Event("Disconnecting.");
			return 0;
		}

		Console.WriteLine();
		if (finished == session.Completion)
		{
			Console.WriteLine("FAILED: the session ended before the first picture.");
			if (session.Completion.Exception?.InnerException is { } ended)
			{
				PrintFailure(ended);
			}
			else if (session.CloseReason is { } reason)
			{
				Console.WriteLine($"The server ended it: {reason}");
			}

			return 1;
		}

		Console.WriteLine("FAILED: connected, but no picture arrived within 30 seconds.");
		return 1;
	}

	/// <summary>Every exception in the chain, with IronRDP's own text where there is one. None of them carries the password.</summary>
	private static void PrintFailure(Exception exception)
	{
		Console.WriteLine(exception is ProtocolConnectException connect ? $"{connect.Failure}: {connect.Message}" : exception.Message);
		Console.WriteLine("Cause:");

		// The first line already holds the message the app would show, so the chain starts with what lies under it.
		Exception? first = exception is ProtocolConnectException && exception.InnerException is not null ? exception.InnerException : exception;
		for (Exception? current = first; current is not null; current = current.InnerException)
		{
			string detail = current switch
			{
				IronRdpException ironRdp => DescribeIronRdp(ironRdp),
				SocketException socket => string.Create(CultureInfo.InvariantCulture, $"{socket.SocketErrorCode} ({socket.ErrorCode}): {socket.Message}"),
				_ => current.Message,
			};
			Console.WriteLine("  " + current.GetType().FullName + ": " + detail);
		}
	}

	private static string DescribeIronRdp(IronRdpException exception)
	{
		try
		{
			return exception.Inner.GetKind() + ": " + exception.Inner.ToDisplay();
		}
		catch (ObjectDisposedException)
		{
			return exception.Message;
		}
	}
}
