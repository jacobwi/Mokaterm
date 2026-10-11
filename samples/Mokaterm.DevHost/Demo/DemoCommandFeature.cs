using System.Buffers;
using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// Commands for the demo machines, so a command run can be looked at without servers. A handful of commands answer
/// the way a small Linux box would (<c>hostname</c>, <c>uptime</c>, <c>echo</c>, <c>sleep 5</c>, <c>exit 3</c>,
/// <c>seq 100000</c> for a lot of output); anything else is "not found" with exit 127. Each machine takes its own
/// short while, so results land one by one.
/// </summary>
internal sealed class DemoCommandFeature : IRemoteCommandFeature
{
	private const int MaxSequence = 5_000_000;

	private readonly DemoAccount _account;
	private readonly string _hostName;
	private readonly TimeProvider _time;

	public DemoCommandFeature(DemoAccount account, string hostName, TimeProvider time)
	{
		_account = account;
		_hostName = hostName;
		_time = time;
	}

	public async Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		long started = _time.GetTimestamp();
		await Task.Delay(TimeSpan.FromMilliseconds(150 + StableNumber(_hostName) % 900), _time, cancellationToken);

		Capture output = new(request.OutputLimitBytes);
		Capture error = new(request.OutputLimitBytes);
		if (DemoSystemOutput.ScriptName(request.Command) is { } script)
		{
			// The SSH module's system view reading this machine: its scripts print an output marker first.
			if (DemoSystemOutput.For(script, _account, _hostName, _time.GetUtcNow()) is { } text)
			{
				output.Text("MOKATERM-OUT\0" + text);
				return Outcome(0, output, error, started);
			}

			error.Line("The demo machine does not run this part of the system view.");
			return Outcome(127, output, error, started);
		}

		int exit = 0;
		foreach (string line in request.Command.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string arguments = string.Join(' ', words.Skip(1));
			switch (words[0])
			{
				case "hostname":
					output.Line(_hostName);
					exit = 0;
					break;
				case "whoami":
					output.Line(_account.Name);
					exit = 0;
					break;
				case "uname":
					output.Line(arguments.Contains('a', StringComparison.Ordinal) ? $"Linux {_hostName} 6.8.0-45-generic #45-Ubuntu SMP x86_64 GNU/Linux" : "Linux");
					exit = 0;
					break;
				case "uptime":
					output.Line(string.Create(CultureInfo.InvariantCulture, $" {_time.GetUtcNow():HH:mm:ss} up {3 + StableNumber(_hostName) % 40} days,  2:17,  1 user,  load average: 0.08, 0.12, 0.10"));
					exit = 0;
					break;
				case "date":
					output.Line(_time.GetUtcNow().ToString("ddd MMM d HH:mm:ss 'UTC' yyyy", CultureInfo.InvariantCulture));
					exit = 0;
					break;
				case "echo":
					output.Line(arguments);
					exit = 0;
					break;
				case "true":
					exit = 0;
					break;
				case "false":
					exit = 1;
					break;
				case "sleep" when double.TryParse(arguments, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds):
					await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600)), _time, cancellationToken);
					exit = 0;
					break;
				case "seq" when int.TryParse(arguments, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count):
					for (int number = 1; number <= Math.Min(count, MaxSequence); number++)
					{
						output.Line(number.ToString(CultureInfo.InvariantCulture));
					}

					exit = 0;
					break;
				case "exit":
					exit = int.TryParse(arguments, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ? code & 0xff : 0;
					return Outcome(exit, output, error, started);
				default:
					error.Line($"sh: 1: {words[0]}: not found");
					exit = 127;
					break;
			}
		}

		return Outcome(exit, output, error, started);
	}

	/// <summary>The same number for the same host on every run, unlike <see cref="string.GetHashCode()"/>.</summary>
	private static int StableNumber(string text)
	{
		int value = 17;
		foreach (char character in text)
		{
			value = unchecked((value * 31) + character);
		}

		return value & 0x7fffffff;
	}

	private RemoteCommandOutcome Outcome(int exit, Capture output, Capture error, long started) => new()
	{
		ExitCode = exit,
		Output = output.ToCaptured(),
		Error = error.ToCaptured(),
		Duration = _time.GetElapsedTime(started),
	};

	/// <summary>Keeps the start of a stream up to the request's cap and counts the rest, like a real channel.</summary>
	private sealed class Capture(int keepBytes)
	{
		private readonly ArrayBufferWriter<byte> _kept = new();
		private long _total;

		public void Line(string text) => Text(text + "\n");

		public void Text(string text)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(text);
			_total += bytes.Length;
			int room = keepBytes - _kept.WrittenCount;
			if (room > 0)
			{
				_kept.Write(bytes.AsSpan(0, Math.Min(room, bytes.Length)));
			}
		}

		public CapturedOutput ToCaptured() => new(_kept.WrittenSpan.ToArray(), _total);
	}
}
