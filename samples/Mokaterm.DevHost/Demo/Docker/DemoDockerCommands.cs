using System.Text;
using Mokaterm.Abstractions.Commands;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// Docker command lines for the demo engine: the line is split the way a POSIX shell splits it (single quotes, the
/// <c>'\''</c> escape, a leading <c>cd -- DIR &amp;&amp;</c>) and handed over as words. A short pause per command keeps
/// the spinners honest.
/// </summary>
internal sealed class DemoDockerCommands : IRemoteCommandFeature
{
	private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(120);

	private readonly DemoDockerEngine _engine;
	private readonly TimeProvider _time;

	public DemoDockerCommands(DemoDockerEngine engine, TimeProvider time)
	{
		_engine = engine;
		_time = time;
	}

	public async Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		long started = _time.GetTimestamp();
		List<string> words = Split(request.Command);
		string? directory = null;
		if (words is ["cd", "--", var folder, "&&", ..])
		{
			directory = folder;
			words.RemoveRange(0, 4);
		}

		// A build or a push runs under the server's own time limit: the command after the seconds is what docker sees.
		if (words is ["sh", "-c", var script, "sh", _, .. var limited] && IsLimitScript(script))
		{
			words = [.. limited];
		}

		if (words is ["docker", "login", .. var login])
		{
			await Task.Delay(TimeSpan.FromSeconds(0.8), _time, cancellationToken);
			string? input = request.Input is { } secret ? Encoding.UTF8.GetString(secret.Span) : null;
			return Outcome(_engine.Login(login, input), request, started);
		}

		if (_engine.Stream([.. words], directory) is { } stream)
		{
			return await StreamAsync(stream, request, started, cancellationToken);
		}

		// Stats take docker about two seconds, since it measures CPU over an interval.
		bool slow = words is ["docker", "stats", ..] or ["docker", "pull", ..] || (words is ["docker", "compose", ..] && words.Contains("up"));
		await Task.Delay(slow ? TimeSpan.FromSeconds(1.5) : Pause, _time, cancellationToken);

		DemoDockerResult result = _engine.Run([.. words], directory);
		return new RemoteCommandOutcome
		{
			ExitCode = result.ExitCode,
			Output = Captured(result.Bytes ?? Encoding.UTF8.GetBytes(result.Output), request.OutputLimitBytes),
			Error = Captured(Encoding.UTF8.GetBytes(result.Error), request.OutputLimitBytes),
			Duration = _time.GetElapsedTime(started),
		};
	}

	/// <summary>The manager's time limit around a build or a push: the limit's probe and function, then the command under it.</summary>
	internal static bool IsLimitScript(string script) =>
		script.StartsWith("if timeout 1 true", StringComparison.Ordinal) && script.EndsWith("mokaterm_limit \"$@\"", StringComparison.Ordinal);

	/// <summary>
	/// Plays a build or a push: each line after its pause, heard live through the request's listener and kept up to the
	/// request's cap as a channel keeps it. A cancel stops between two lines, before the change is made.
	/// </summary>
	private async Task<RemoteCommandOutcome> StreamAsync(DemoDockerStream stream, RemoteCommandRequest request, long started, CancellationToken cancellationToken)
	{
		using MemoryStream output = new();
		using MemoryStream error = new();
		foreach (DemoDockerLine line in stream.Steps)
		{
			await Task.Delay(line.Pause, _time, cancellationToken);
			byte[] bytes = Encoding.UTF8.GetBytes(line.Text + "\n");
			request.OnOutput?.Invoke(line.IsError ? CommandStream.Error : CommandStream.Output, bytes);
			await (line.IsError ? error : output).WriteAsync(bytes, cancellationToken);
		}

		int exit = stream.Finish();
		return new RemoteCommandOutcome
		{
			ExitCode = exit,
			Output = Captured(output.ToArray(), request.OutputLimitBytes),
			Error = Captured(error.ToArray(), request.OutputLimitBytes),
			Duration = _time.GetElapsedTime(started),
		};
	}

	private RemoteCommandOutcome Outcome(DemoDockerResult result, RemoteCommandRequest request, long started) => new()
	{
		ExitCode = result.ExitCode,
		Output = Captured(result.Bytes ?? Encoding.UTF8.GetBytes(result.Output), request.OutputLimitBytes),
		Error = Captured(Encoding.UTF8.GetBytes(result.Error), request.OutputLimitBytes),
		Duration = _time.GetElapsedTime(started),
	};

	/// <summary>POSIX word splitting for the lines the manager builds: bare words and single-quoted ones.</summary>
	internal static List<string> Split(string line)
	{
		List<string> words = [];
		StringBuilder word = new();
		bool inWord = false;
		bool quoted = false;
		for (int index = 0; index < line.Length; index++)
		{
			char character = line[index];
			if (quoted)
			{
				if (character == '\'')
				{
					quoted = false;
				}
				else
				{
					word.Append(character);
				}
			}
			else if (character == '\'')
			{
				quoted = true;
				inWord = true;
			}
			else if (character == '\\' && index + 1 < line.Length)
			{
				word.Append(line[++index]);
				inWord = true;
			}
			else if (char.IsWhiteSpace(character))
			{
				if (inWord)
				{
					words.Add(word.ToString());
					word.Clear();
					inWord = false;
				}
			}
			else
			{
				word.Append(character);
				inWord = true;
			}
		}

		if (inWord)
		{
			words.Add(word.ToString());
		}

		return words;
	}

	private static CapturedOutput Captured(byte[] bytes, int keep)
	{
		return new CapturedOutput(bytes.AsMemory(0, Math.Min(bytes.Length, Math.Max(keep, RemoteCommandRequest.MinOutputLimitBytes))), bytes.Length);
	}
}
