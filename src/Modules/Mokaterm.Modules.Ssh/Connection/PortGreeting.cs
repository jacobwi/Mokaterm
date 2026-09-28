using System.Net.Sockets;
using System.Text;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// Reads the first bytes a port answers with, to name the protocol it really speaks. A port that belongs to something
/// else (a VNC display typed into an SSH login) looks like a broken server otherwise. Only runs after a failed connect.
/// </summary>
internal static class PortGreeting
{
	// Every greeting looked for here fits in the first few bytes.
	private const int MaxBytes = 16;

	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

	/// <summary>True for the failures that can mean the port is not an SSH port at all.</summary>
	public static bool MayBeWrongProtocol(ConnectFailure failure) =>
		failure is ConnectFailure.HostUnreachable or ConnectFailure.ProtocolError;

	/// <summary>
	/// <paramref name="failure"/> with a sentence naming the protocol the port greets with, or unchanged when the port
	/// stays silent, greets with something unknown, or cannot be reached again.
	/// </summary>
	public static async Task<ProtocolConnectException> ExplainAsync(
		ProtocolConnectException failure,
		string host,
		int port,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(failure);
		string? protocol = await DescribeAsync(host, port, cancellationToken);
		return protocol is null
			? failure
			: new ProtocolConnectException(
				failure.Failure,
				$"{failure.Message.TrimEnd()} The port answers like {protocol}, so open it with that protocol instead of SSH.",
				failure);
	}

	private static async Task<string?> DescribeAsync(string host, int port, CancellationToken cancellationToken)
	{
		try
		{
			using CancellationTokenSource probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			probe.CancelAfter(ProbeTimeout);
			using TcpClient client = new();
			await client.ConnectAsync(host, port, probe.Token);
			byte[] buffer = new byte[MaxBytes];
			int read = await client.GetStream().ReadAsync(buffer, probe.Token);
			return read > 0 ? Recognize(buffer.AsSpan(0, read)) : null;
		}
		catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or OperationCanceledException or ArgumentException)
		{
			// The diagnosis is a nicety; the original failure is what matters, even when the address or port is invalid.
			return null;
		}
	}

	private static string? Recognize(ReadOnlySpan<byte> greeting)
	{
		// ASCII is enough: every greeting looked for is ASCII, and other bytes simply do not match.
		string text = Encoding.ASCII.GetString(greeting).TrimStart();
		if (text.StartsWith("RFB ", StringComparison.Ordinal))
		{
			return "a VNC server";
		}

		if (text.StartsWith("220", StringComparison.Ordinal))
		{
			return "an FTP server";
		}

		if (text.StartsWith("HTTP/", StringComparison.Ordinal))
		{
			return "a web server";
		}

		// An SSH greeting means the port is right and something else went wrong.
		return null;
	}
}
