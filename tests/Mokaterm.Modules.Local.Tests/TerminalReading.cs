using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>Reads what a terminal channel shows, the way the view's pump does, and never waits without a limit.</summary>
internal static class TerminalReading
{
	/// <summary>Everything until the channel says it has ended.</summary>
	public static async Task<string> ReadToEndAsync(ITerminalChannel channel, TimeSpan? limit = null)
	{
		using CancellationTokenSource deadline = Deadline(limit);
		StringBuilder text = new();
		byte[] buffer = new byte[4096];
		while (true)
		{
			int read = await channel.ReadAsync(buffer, deadline.Token);
			if (read == 0)
			{
				return text.ToString();
			}

			text.Append(Encoding.UTF8.GetString(buffer, 0, read));
		}
	}

	/// <summary>Reads until <paramref name="expected"/> has appeared, and returns everything read so far.</summary>
	public static async Task<string> ReadUntilAsync(ITerminalChannel channel, string expected, TimeSpan? limit = null)
	{
		using CancellationTokenSource deadline = Deadline(limit);
		StringBuilder text = new();
		byte[] buffer = new byte[4096];
		while (!text.ToString().Contains(expected, StringComparison.Ordinal))
		{
			int read = await channel.ReadAsync(buffer, deadline.Token);
			if (read == 0)
			{
				Assert.Fail($"The terminal ended before showing {expected}. It showed: {text}");
			}

			text.Append(Encoding.UTF8.GetString(buffer, 0, read));
		}

		return text.ToString();
	}

	private static CancellationTokenSource Deadline(TimeSpan? limit)
	{
		CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(limit ?? Eventually.Limit);
		return deadline;
	}
}
