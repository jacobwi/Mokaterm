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
			},
			TimeSpan.FromSeconds(arguments.TimeoutSeconds),
			new ConsoleCertificateCheck(output, arguments.AcceptCertificate));

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
			PrintFailure(ex);
			return 1;
		}

		RdpConnectionInfo info = transport.Describe();
		output.Event(string.Create(CultureInfo.InvariantCulture, $"Connected: {info.Width} x {info.Height}, {info.Security}"));
		if (arguments.ConnectOnly)
		{
			await transport.DisposeAsync();
			return 0;
		}

		return await WaitForPictureAsync(transport, output, stop.Token);
	}

	private static void PrintHeader(ProbeArguments arguments)
	{
		string version = typeof(Connection).Assembly.GetName().Version?.ToString() ?? "unknown";
		Console.WriteLine("Mokaterm RDP probe, IronRDP " + version + ", " + Environment.OSVersion.VersionString);
		bool login = arguments.User is not null;
		string who = login ? "user " + arguments.User : "no login";
		string domain = string.IsNullOrEmpty(arguments.Domain) ? "" : ", domain " + arguments.Domain;
		string nla = arguments.NetworkLevelAuthentication && login ? "on" : "off";
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

			// Without network level authentication no password is sent, so none is asked for.
			Password = arguments.NetworkLevelAuthentication ? PasswordPrompt.Read(user) : null,
		};
	}

	/// <summary>Runs the session the way the app's view does and waits for the first region the server draws.</summary>
	private static async Task<int> WaitForPictureAsync(RdpTransport transport, ProbeOutput output, CancellationToken cancellationToken)
	{
		await using RdpSession session = new(transport, new RdpSettings(), null, NullLogger.Instance);
		FirstPictureSink sink = new();
		await using IRdpChannel channel = await session.AttachAsync(sink, cancellationToken);
		await channel.StartAsync(cancellationToken);
		Task finished = await Task.WhenAny(sink.FirstFrame, session.Completion, Task.Delay(PictureWait, cancellationToken));
		if (finished == sink.FirstFrame)
		{
			int bytes = await sink.FirstFrame;
			output.Event(string.Create(CultureInfo.InvariantCulture, $"First picture: {bytes} bytes. Disconnecting."));
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
