using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Maui.Services;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Restarts the desktop app. The new copy starts with <see cref="AfterArgument"/> and this process's id and waits for it
/// to exit (<see cref="WaitForPreviousRun"/>), because until then this one holds the single instance key and the data
/// folder lock, and a new copy that found them taken would hand over to the one that is closing.
/// </summary>
internal sealed class DesktopRestarter : IAppRestarter
{
	public const string AfterArgument = "--restart-after";

	private readonly DesktopSessions _sessions;
	private readonly ILogger<DesktopRestarter> _logger;

	public DesktopRestarter(DesktopSessions sessions, ILogger<DesktopRestarter> logger)
	{
		_sessions = sessions;
		_logger = logger;
	}

	public async Task<bool> RestartAsync()
	{
		string arguments = $"{AfterArgument} {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}";
#if MOKATERM_PACKAGED
		// A packaged app cannot start its own exe by path; the platform restarts it, and ends this process on success.
		await _sessions.ShutDownAsync().ConfigureAwait(false);
		global::Windows.ApplicationModel.Core.AppRestartFailureReason reason = Microsoft.Windows.AppLifecycle.AppInstance.Restart(arguments);
		_logger.LogWarning("Restarting the app failed: {Reason}", reason);
		return false;
#else
		try
		{
			using Process? started = Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("The app's path is unknown."))
			{
				Arguments = arguments,
				UseShellExecute = false,
			});
		}
		catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
		{
			_logger.LogWarning("Restarting the app failed: {Failure}", LogSafe.Describe(ex));
			return false;
		}

		await _sessions.ShutDownAsync().ConfigureAwait(false);
		MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
		return true;
#endif
	}

	/// <summary>
	/// In a copy started by <see cref="RestartAsync"/>, waits for the copy that started it to exit. Runs first thing in
	/// <c>Main</c>, before the single instance check.
	/// </summary>
	public static void WaitForPreviousRun(string[] arguments)
	{
		int index = Array.IndexOf(arguments, AfterArgument);
		if (index < 0
			|| index + 1 >= arguments.Length
			|| !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int processId))
		{
			return;
		}

		try
		{
			using Process previous = Process.GetProcessById(processId);
			previous.WaitForExit(TimeSpan.FromSeconds(20));
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
		{
			// Already gone, which is what we were waiting for.
		}
	}
}
