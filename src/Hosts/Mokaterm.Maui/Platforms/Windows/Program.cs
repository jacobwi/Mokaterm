using Microsoft.UI.Dispatching;
using Mokaterm.Maui.Platforms.Windows;
using Mokaterm.Maui.Services;
using Velopack;
using Velopack.Locators;

namespace Mokaterm.Maui.WinUI;

/// <summary>
/// The Windows entry point, in place of the one the XAML compiler generates (DISABLE_XAML_GENERATED_MAIN in the project):
/// the installer's hooks, the data folder checks and the single instance check all run before WinUI starts and before
/// anything is written to the data folder.
/// </summary>
public static class Program
{
	[STAThread]
	private static int Main()
	{
		// First and exactly once, as Velopack requires. An install, update or uninstall hook runs in here and ends the
		// process before the app does anything else.
		VelopackApp.Build().Run();

		WinRT.ComWrappersSupport.InitializeComWrappers();

		if (StartupProblem() is { } problem)
		{
			NativeMessageBox.ShowError(problem);
			return 1;
		}

		// Both checks guard against a rare mistake; one that cannot be made is no reason not to start, so a failure is only
		// written down once the crash log is set up.
		Exception? handOverFailure = null;
		try
		{
			if (SingleInstance.HandOver(DesktopPaths.Root))
			{
				return 0;
			}
		}
		catch (Exception ex)
		{
			handOverFailure = ex;
		}

		bool folderFree = true;
		Exception? lockFailure = null;
		try
		{
			folderFree = SingleInstance.LockDataFolder(DesktopPaths.Root);
		}
		catch (Exception ex)
		{
			lockFailure = ex;
		}

		if (!folderFree)
		{
			NativeMessageBox.ShowError(
				"Another copy of Mokaterm is already running with this data folder, started from a different place. " +
				"Close it before starting this one.");
			return 1;
		}

		CrashLog.Install(DesktopPaths.LogDirectory);
		if (handOverFailure is not null)
		{
			CrashLog.Write("SingleInstance.HandOver", handOverFailure, terminating: false);
		}

		if (lockFailure is not null)
		{
			CrashLog.Write("SingleInstance.LockDataFolder", lockFailure, terminating: false);
		}

		global::Microsoft.UI.Xaml.Application.Start(parameters =>
		{
			DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
			SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
			SingleInstance.ListenOn(queue);
			_ = new App();
		});

		return 0;
	}

	/// <summary>Why this run must not start, or null. Nothing here writes to disk.</summary>
	private static string? StartupProblem()
	{
		if (DesktopPaths.OverrideProblem is { } problem)
		{
			return problem;
		}

		// An uninstall deletes the install folder, and a repair or reinstall replaces it; a data folder inside it would
		// go with it. The package id keeps the default locations apart, so this only catches an install to a chosen
		// folder. A portable copy is never uninstalled, so it may keep its data next to it.
		IVelopackLocator installation = VelopackLocator.Current;
		if (installation.RootAppDir is { } installFolder && !installation.IsPortable && IsSameOrInside(DesktopPaths.Root, installFolder))
		{
			return $"Mokaterm is installed in the folder that holds its data ({DesktopPaths.Root}). Uninstalling or " +
				"repairing it would delete the vault with it. Install it again to its default location.";
		}

		return null;
	}

	private static bool IsSameOrInside(string path, string folder)
	{
		string inner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		string outer = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
		return inner.Equals(outer, StringComparison.OrdinalIgnoreCase)
			|| inner.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
