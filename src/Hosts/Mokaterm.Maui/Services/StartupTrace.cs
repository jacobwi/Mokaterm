using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Mokaterm.Maui.Services;

// With MOKATERM_STARTUP_TRACE=1 a start writes logs/startup-*.log once the page first rendered: every step's time since
// Windows created the process, and since the step before. Release builds log nothing else, so this is how a slow start
// is read on a machine without a debugger.
internal static class StartupTrace
{
	private const string Variable = "MOKATERM_STARTUP_TRACE";

	private static readonly bool Enabled = Environment.GetEnvironmentVariable(Variable) == "1";
	private static readonly Lock Gate = new();
	private static readonly List<(string Step, TimeSpan At)> Marks = [];
	private static readonly long Origin = Stopwatch.GetTimestamp();
	private static readonly TimeSpan BeforeOrigin = Enabled ? DateTime.Now - Process.GetCurrentProcess().StartTime : TimeSpan.Zero;
	private static bool _written;

	public static void Mark(string step)
	{
		if (!Enabled)
		{
			return;
		}

		TimeSpan at = BeforeOrigin + Stopwatch.GetElapsedTime(Origin);
		lock (Gate)
		{
			if (!_written)
			{
				Marks.Add((step, at));
			}
		}
	}

	public static void Write()
	{
		if (!Enabled)
		{
			return;
		}

		StringBuilder text = new();
		lock (Gate)
		{
			if (_written)
			{
				return;
			}

			_written = true;
			TimeSpan previous = TimeSpan.Zero;
			foreach ((string step, TimeSpan at) in Marks)
			{
				text.AppendLine(CultureInfo.InvariantCulture, $"{at.TotalMilliseconds,8:F0} ms  {(at - previous).TotalMilliseconds,6:F0} ms  {step}");
				previous = at;
			}
		}

		// A trace that cannot be written must not stop the start it measured.
		try
		{
			Directory.CreateDirectory(DesktopPaths.LogDirectory);
			string name = $"startup-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log";
			File.WriteAllText(Path.Combine(DesktopPaths.LogDirectory, name), text.ToString());
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
