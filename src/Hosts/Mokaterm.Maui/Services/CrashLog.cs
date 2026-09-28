using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Mokaterm.Maui.Services;

/// <summary>
/// Writes exceptions that nothing else caught to <see cref="DesktopPaths.LogDirectory"/>, one file per run, so a crash
/// on a machine without a debugger still leaves something to read. An entry keeps exception types, HRESULTs and stack
/// traces and leaves the messages out: a message can name a host, a user or a path, and the vault keeps those out of
/// plain files. Release builds have no other log, because the desktop host registers no logging provider there.
/// </summary>
internal static class CrashLog
{
	private const string FilePrefix = "crash-";
	private const int FilesToKeep = 10;

	// Unobserved task exceptions can repeat; a run that keeps failing should not fill the disk.
	private const int EntriesPerRun = 100;

	// Aggregates of aggregates are rare; this only guards against a pathological chain.
	private const int MaxDepth = 8;

	private static readonly Lock Gate = new();
	private static string? _directory;
	private static string? _file;
	private static int _entries;

	/// <summary>Hooks the process-wide handlers. The platform entry point calls this first, before anything else runs.</summary>
	public static void Install(string directory)
	{
		lock (Gate)
		{
			if (_directory is not null)
			{
				return;
			}

			_directory = directory;
		}

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			Write("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);

		// Blazor rethrows an unhandled component exception on MAUI's dispatcher inside a task nobody awaits, so this is
		// where those end up too, once the garbage collector finds the task.
		TaskScheduler.UnobservedTaskException += (_, e) =>
			Write("TaskScheduler.UnobservedTaskException", e.Exception, terminating: false);
	}

	public static void Write(string source, Exception? exception, bool terminating)
	{
		try
		{
			StringBuilder entry = new();
			entry.Append(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:O}  {source}");
			entry.AppendLine(terminating ? "  (the app ends here)" : string.Empty);
			AppendException(entry, exception, 0);
			entry.AppendLine();

			lock (Gate)
			{
				if (_directory is null || _entries >= EntriesPerRun)
				{
					return;
				}

				_file ??= StartFile(_directory);
				File.AppendAllText(_file, entry.ToString());
				_entries++;
			}
		}
		catch (Exception)
		{
			// A crash log that throws would replace the failure it was about to record with its own.
		}
	}

	private static string StartFile(string directory)
	{
		Directory.CreateDirectory(directory);
		RemoveOldFiles(directory);

		// UTC timestamps in the name sort the files by age, which RemoveOldFiles relies on.
		string path = Path.Combine(
			directory,
			string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log"));

		StringBuilder header = new();
		header.Append(CultureInfo.InvariantCulture, $"Mokaterm {MauiAppEnvironment.Version}, {RuntimeInformation.FrameworkDescription}");
		header.Append(CultureInfo.InvariantCulture, $", {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}").AppendLine();
		header.AppendLine("Exceptions nothing else caught, without their messages. Entries marked as ending the app are crashes.");
		header.AppendLine();
		File.AppendAllText(path, header.ToString());
		return path;
	}

	private static void RemoveOldFiles(string directory)
	{
		string[] files = Directory.GetFiles(directory, FilePrefix + "*.log");
		Array.Sort(files, StringComparer.Ordinal);

		// Leaves room for the file about to be written.
		for (int i = 0; i < files.Length - (FilesToKeep - 1); i++)
		{
			try
			{
				File.Delete(files[i]);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	private static void AppendException(StringBuilder entry, Exception? exception, int depth)
	{
		string indent = new(' ', depth * 4);
		if (exception is null)
		{
			entry.Append(indent).AppendLine("(no exception object)");
			return;
		}

		entry.Append(indent).Append(CultureInfo.InvariantCulture, $"{exception.GetType().FullName} (HRESULT 0x{exception.HResult:X8})").AppendLine();
		if (exception.StackTrace is { } stackTrace)
		{
			foreach (ReadOnlySpan<char> line in stackTrace.AsSpan().EnumerateLines())
			{
				entry.Append(indent).Append(line).AppendLine();
			}
		}

		if (depth >= MaxDepth)
		{
			return;
		}

		if (exception is AggregateException aggregate)
		{
			foreach (Exception child in aggregate.InnerExceptions)
			{
				AppendException(entry, child, depth + 1);
			}
		}
		else if (exception.InnerException is { } cause)
		{
			AppendException(entry, cause, depth + 1);
		}
	}
}
