using System.Net.Sockets;
using System.Security.Authentication;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Turns connect failures into <see cref="ProtocolConnectException"/> with messages written for the user. Every
/// message is worded for the step the attempt reached (<see cref="RdpConnectProgress"/>), since the same timeout means
/// a firewall while dialing and a domain controller nobody can reach while logging in.
/// </summary>
internal static class RdpConnectErrors
{
	// Standard RDP Security's keys come from the server's own proprietary certificate or a self-signed one, which no
	// known hosts check covers, and it would carry an automatic sign-in's password.
	private const string StandardSecurity =
		"whose RC4 encryption proves nothing about the server, so this client sent nothing more. On that PC, set \"Require " +
		"use of specific security layer for remote (RDP) connections\" to Negotiate or SSL.";

	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Remote Desktop listens on 3389 and has to be turned on for the account.",
		TimedOut = "Remote Desktop has to be turned on at that PC and allowed through its firewall, and Windows Home " +
			"editions cannot accept Remote Desktop connections at all. Check the port as well: Remote Desktop listens on " +
			"3389 unless someone changed it.",
	};

	/// <summary>
	/// Words a server puts in a CredSSP failure when the account is the problem. IronRDP passes the operating
	/// system's own text through (STATUS_LOGON_FAILURE and the like), and only some of it arrives as a typed error kind.
	/// </summary>
	private static readonly string[] LoginWords =
	[
		"logon",
		"log on",
		"password",
		"credential",
		"authentication failed",
		"access denied",
		"account",
		"unknown user",
	];

	/// <summary>
	/// True when a failure while checking the login is about the account rather than the connection, so another
	/// password may work. <paramref name="loginMessagesSent"/> counts the CredSSP messages that went out: a server that
	/// hangs up once it has the second one (the NTLM answer) is refusing the login, which is how a Windows server
	/// without CredSSP error codes says it.
	/// </summary>
	public static bool IsLoginRefused(Exception exception, int loginMessagesSent)
	{
		ArgumentNullException.ThrowIfNull(exception);
		if (exception is RdpAuthenticationException)
		{
			return true;
		}

		if (exception is IronRdpException ironRdp)
		{
			string text = Describe(ironRdp);
			if (IsUnreadableName(text))
			{
				return false;
			}

			return Kind(ironRdp) is IronRdpErrorKind.CredsspError or IronRdpErrorKind.AccessDenied
				|| LoginWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
		}

		return loginMessagesSent >= 2 && IsConnectionLoss(exception);
	}

	/// <summary>The connection went away under the attempt, as opposed to the server saying something wrong.</summary>
	public static bool IsConnectionLoss(Exception exception) => exception switch
	{
		EndOfStreamException => true,
		SocketException socket => socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown,
		IOException io when io.InnerException is SocketException inner => IsConnectionLoss(inner),
		IOException => true,
		_ => false,
	};

	/// <summary>What the user is told when the server turns the login down.</summary>
	public static string LoginRefused(Exception exception, RdpConnectProgress progress, RdpLogin login)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(progress);
		string message = exception is IronRdpException ironRdp
			? $"{progress.Endpoint} refused the login: {Describe(ironRdp)}"
			: $"{progress.Endpoint} closed the connection once it had the login, which usually means the user name or the password is wrong.";
		if (login.CloudAccountAdvice is { } advice)
		{
			return message + " " + advice;
		}

		// The CredSSP library sends you@example.com as the account "you" in the domain "example.com", which a
		// Microsoft account does not answer to; the PC's own name for the account does.
		return login.IsBarePrincipalName
			? message + " If that PC signs in with a Microsoft account, run whoami on it and use the name it prints (such as " +
				"DESKTOP-ABC\\name) with the Microsoft account's password, not its PIN."
			: message;
	}

	/// <summary>
	/// What the user is told when the server refuses the RDP request, by RDP_NEG_FAILURE's code (MS-RDPBCGR 2.2.1.2.2).
	/// <paramref name="route"/> says what was asked for: CredSSP and TLS for network level authentication, TLS alone
	/// otherwise.
	/// </summary>
	public static string NegotiationRefused(uint code, RdpConnectProgress progress, RdpSignInRoute route)
	{
		ArgumentNullException.ThrowIfNull(progress);
		string endpoint = progress.Endpoint;
		string name = RdpNegotiation.FailureName(code);
		bool nla = route == RdpSignInRoute.NetworkLevelAuthentication;
		return code switch
		{
			RdpNegotiation.HybridRequiredByServer when route == RdpSignInRoute.Automatic =>
				$"{endpoint} requires network level authentication ({name}), which is off for this login. Turn \"Network " +
				"level authentication\" back on in this login's RDP options, or turn off \"Require devices to use Network " +
				"Level Authentication to connect\" on that PC (Settings > System > Remote Desktop).",
			RdpNegotiation.HybridRequiredByServer when !nla =>
				$"{endpoint} requires network level authentication ({name}), which checks the login's name and password " +
				"before the session starts. Set the authentication to Password and turn \"Network level authentication\" on " +
				"in this login's RDP options, or turn off \"Require devices to use Network Level Authentication to connect\" " +
				"on that PC (Settings > System > Remote Desktop).",
			RdpNegotiation.SslRequiredByServer when nla =>
				$"{endpoint} only accepts TLS without network level authentication ({name}). Turn \"Network level " +
				"authentication\" off in this login's RDP options.",
			RdpNegotiation.SslRequiredByServer =>
				$"{endpoint} said it requires TLS ({name}), which is what this client asked for. Check the security layer " +
				"Remote Desktop is set to use on that PC.",
			RdpNegotiation.SslNotAllowedByServer =>
				$"{endpoint} only allows Standard RDP Security ({name}), {StandardSecurity}",
			RdpNegotiation.SslCertNotOnServer =>
				$"{endpoint} has no certificate for Remote Desktop ({name}), so it cannot start TLS. Restarting the Remote " +
				"Desktop Services service on that PC usually makes it create a new one.",
			RdpNegotiation.SslWithUserAuthRequiredByServer =>
				$"{endpoint} wants a client certificate ({name}), which this client cannot send.",
			_ => $"{endpoint} refused the RDP request ({name}).",
		};
	}

	/// <summary>
	/// What the user is told when the server picked a security protocol the request did not offer, which stops the
	/// attempt before the TLS handshake and so before any login is sent.
	/// </summary>
	public static string UnrequestedProtocol(RdpSecurityProtocols selected, RdpConnectProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);
		return selected == RdpSecurityProtocols.Rdp
			? $"{progress.Endpoint} offered only Standard RDP Security, {StandardSecurity}"
			: $"{progress.Endpoint} picked {RdpNegotiation.Name(selected)}, which this client did not ask for, so it sent nothing more.";
	}

	/// <summary>
	/// The exception the shell sees for <paramref name="exception"/>, worded for the step in <paramref name="progress"/>.
	/// <paramref name="timedOut"/> says the attempt's deadline ran out, whatever the step happened to throw on the way out.
	/// </summary>
	public static async Task<ProtocolConnectException> DescribeAsync(
		Exception exception,
		RdpConnectProgress progress,
		bool timedOut,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(progress);
		string endpoint = progress.Endpoint;
		if (exception is ProtocolConnectException connect)
		{
			return connect;
		}

		if (timedOut || exception is TimeoutException)
		{
			return progress.Stage == RdpConnectStage.Dialing
				? await FromSocketErrorAsync(new SocketException((int)SocketError.TimedOut), progress, cancellationToken)
				: new ProtocolConnectException(ConnectFailure.Timeout, TimedOut(progress), exception);
		}

		return exception switch
		{
			RdpAuthenticationException auth => new(ConnectFailure.AuthenticationFailed, auth.Message, auth),
			IronRdpException ironRdp => FromIronRdp(ironRdp, progress),
			AuthenticationException tls => new(ConnectFailure.ProtocolError, $"The TLS handshake with {endpoint} failed: {tls.Message}", tls),
			SocketException socket => await FromSocketErrorAsync(socket, progress, cancellationToken),
			EndOfStreamException => new(ConnectFailure.ProtocolError, ClosedWhile(progress), exception),
			IOException io when io.InnerException is SocketException inner => await FromSocketErrorAsync(inner, progress, cancellationToken),
			IOException io => new(ConnectFailure.ProtocolError, $"The connection to {endpoint} broke: {io.Message}", io),
			InvalidDataException data => new(ConnectFailure.ProtocolError, $"{endpoint} sent something this client cannot use: {data.Message}", data),
			_ => new(ConnectFailure.Unknown, $"Could not connect to {endpoint}: {exception.Message}", exception),
		};
	}

	/// <summary>The sentence for an attempt whose deadline ran out at the step <paramref name="progress"/> is on.</summary>
	public static string TimedOut(RdpConnectProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);
		string endpoint = progress.Endpoint;
		return progress.Stage switch
		{
			RdpConnectStage.Dialing => $"{endpoint} did not answer in time.",
			RdpConnectStage.Negotiating => $"{endpoint} took the connection but did not answer the RDP request in time. Check that this port belongs to Remote Desktop.",
			RdpConnectStage.Tls => $"{endpoint} did not finish the TLS handshake in time.",
			RdpConnectStage.Login => $"{endpoint} did not finish checking the login (CredSSP) in time.",
			RdpConnectStage.Kerberos => RdpKerberosClient.Unreachable(progress.Detail ?? "for this login", "did not answer in time"),
			RdpConnectStage.Session => progress.Detail is null
				? $"{endpoint} did not finish setting up the session in time."
				: $"{endpoint} did not finish setting up the session in time. It stopped answering at {progress.Detail}.",
			_ => $"{endpoint} did not finish the RDP connection in time.",
		};
	}

	private static string ClosedWhile(RdpConnectProgress progress)
	{
		string endpoint = progress.Endpoint;
		return progress.Stage switch
		{
			RdpConnectStage.Negotiating => $"{endpoint} closed the connection without answering the RDP request. Check that this port belongs to Remote Desktop.",
			RdpConnectStage.Tls => $"{endpoint} closed the connection during the TLS handshake.",
			RdpConnectStage.Login => $"{endpoint} closed the connection while checking the login.",
			RdpConnectStage.Session => progress.Detail is null
				? $"{endpoint} closed the connection while setting up the session."
				: $"{endpoint} closed the connection while setting up the session, at {progress.Detail}.",
			_ => $"{endpoint} closed the connection while connecting.",
		};
	}

	private static IronRdpErrorKind? Kind(IronRdpException exception)
	{
		try
		{
			return exception.Inner.GetKind();
		}
		catch (ObjectDisposedException)
		{
			return null;
		}
	}

	/// <summary>IronRDP's own text for a failure, for messages the user reads. Logs take <c>LogSafe.Describe</c> instead.</summary>
	public static string Describe(IronRdpException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		try
		{
			return exception.Inner.ToDisplay();
		}
		catch (ObjectDisposedException)
		{
			return exception.Message;
		}
	}

	// IronRDP and sspi-rs refuse a name they cannot split into an account and a domain before anything is sent.
	// RdpLogin.CredsspRefusal catches the known forms first; this is for anything it missed.
	private static bool IsUnreadableName(string text) =>
		text.Contains("mixed username format", StringComparison.OrdinalIgnoreCase)
		|| text.Contains("invalid username", StringComparison.OrdinalIgnoreCase);

	private static ProtocolConnectException FromIronRdp(IronRdpException exception, RdpConnectProgress progress)
	{
		string endpoint = progress.Endpoint;
		string detail = Describe(exception);
		if (IsUnreadableName(detail))
		{
			return new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"The user name could not be read as a Windows login. Write it as name, DOMAIN\\name or name@domain.",
				exception);
		}

		IronRdpErrorKind? kind = Kind(exception);
		if (progress.Stage == RdpConnectStage.Negotiating)
		{
			if (kind is IronRdpErrorKind.DecodeError or IronRdpErrorKind.PduError)
			{
				return new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"{endpoint} answered with something that is not RDP ({detail}). Check the port.",
					exception);
			}

			// The server's answer was read before IronRDP saw it (RdpConnectSequence.CheckAnswer), and refusals are
			// worded by their code there, so what reaches this point is IronRDP's own objection.
			return new ProtocolConnectException(ConnectFailure.ProtocolError, $"{endpoint} refused the RDP request: {detail}.", exception);
		}

		return progress.Stage switch
		{
			RdpConnectStage.Login or RdpConnectStage.Kerberos => new(
				ConnectFailure.AuthenticationFailed,
				$"Checking the login with {endpoint} failed: {detail}",
				exception),
			_ when kind == IronRdpErrorKind.WrongOS => new(
				ConnectFailure.Unknown,
				$"This part of RDP does not run on this operating system: {detail}",
				exception),
			_ => new(ConnectFailure.ProtocolError, $"Could not open an RDP session on {endpoint}: {detail}", exception),
		};
	}

	private static async Task<ProtocolConnectException> FromSocketErrorAsync(SocketException socket, RdpConnectProgress progress, CancellationToken cancellationToken) =>
		(await SocketFailures.DescribeAsync(socket, progress.Host, progress.Endpoint, Hints, cancellationToken)).ToException(socket);
}
