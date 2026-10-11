using System.Security.Cryptography;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Relays one forwarded agent channel a message at a time: a whole request from the server's program, then the agent's
/// whole answer. The protocol is strictly request then answer, so nothing is lost by waiting, and every message is
/// checked against the protocol's framing on the way through.
/// </summary>
internal static class AgentRelay
{
	private const int ReadBytes = 16 * 1024;

	/// <summary>Runs until the program hangs up, a message breaks the framing or the agent stops answering.</summary>
	/// <exception cref="Agent.SshAgentException">The agent went away mid request.</exception>
	public static async Task RunAsync(SshForwardedChannel channel, IForwardedAgent agent, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(channel);
		ArgumentNullException.ThrowIfNull(agent);
		AgentFrameReader frames = new();
		byte[] buffer = new byte[ReadBytes];
		try
		{
			while (true)
			{
				int read = await channel.ReadAsync(buffer, cancellationToken);
				if (read == 0 || !frames.Append(buffer.AsSpan(0, read)))
				{
					return;
				}

				while (frames.TryTake(out byte[]? request))
				{
					byte[] answer;
					try
					{
						answer = await agent.ExchangeAsync(request, cancellationToken);
					}
					finally
					{
						// A request can carry a private key on its way into the agent (ssh-add on the server).
						CryptographicOperations.ZeroMemory(request);
					}

					if (answer.Length is 0 or > AgentFrameReader.MaxMessageBytes
						|| !await channel.WriteAsync(AgentFrameReader.Frame(answer), cancellationToken))
					{
						return;
					}
				}

				if (frames.IsBroken)
				{
					return;
				}
			}
		}
		finally
		{
			frames.Clear();
			CryptographicOperations.ZeroMemory(buffer);
		}
	}
}
