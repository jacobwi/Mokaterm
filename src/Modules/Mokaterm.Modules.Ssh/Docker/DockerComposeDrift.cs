using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

// The compose files' service hashes per project, read for the session and kept: config --hash parses every file, so
// it is read when a page asks and the answer it has is older than that page wants, never on the pages' own polls.
// What the containers' labels say is compared afresh at every draw (ComposeDrift.Compare), so an up that made the
// containers again shows at once, with no read.
internal sealed class DockerComposeDrift
{
	// How old an answer the project list takes, which reads a project or two per refresh.
	public static readonly TimeSpan ListAge = TimeSpan.FromMinutes(5);

	// How old an answer an open project takes.
	public static readonly TimeSpan ProjectAge = TimeSpan.FromMinutes(1);

	private readonly TimeProvider _time;
	private readonly Lock _lock = new();
	private readonly Dictionary<string, ComposeHashes> _read = new(StringComparer.Ordinal);
	private readonly HashSet<string> _reading = new(StringComparer.Ordinal);

	public DockerComposeDrift(TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(time);
		_time = time;
	}

	public ComposeHashes? Find(string project)
	{
		ArgumentNullException.ThrowIfNull(project);
		lock (_lock)
		{
			return _read.GetValueOrDefault(project);
		}
	}

	public bool IsStale(string project, ComposeTool tool, TimeSpan age)
	{
		ArgumentNullException.ThrowIfNull(tool);
		lock (_lock)
		{
			return !_reading.Contains(project)
				&& (!_read.TryGetValue(project, out ComposeHashes? hashes) || hashes.Tool != tool.Display || _time.GetUtcNow() - hashes.ReadAt >= age);
		}
	}

	// Drops what a project's files said, after the manager saved one of them.
	public void Forget(string project)
	{
		ArgumentNullException.ThrowIfNull(project);
		lock (_lock)
		{
			_read.Remove(project);
		}
	}

	// Reads the project's hashes and keeps them. A failure is kept too, as the reason nothing can be told; null when the
	// project has no name or another read of it is already on its way.
	public async Task<ComposeHashes?> ReadAsync(DockerClient client, ComposeTarget target, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(target);
		if (target.Project is not { } project)
		{
			return null;
		}

		lock (_lock)
		{
			if (!_reading.Add(project))
			{
				return null;
			}
		}

		try
		{
			ComposeHashes hashes = await ReadHashesAsync(client, target, cancellationToken);
			lock (_lock)
			{
				_read[project] = hashes;
			}

			return hashes;
		}
		finally
		{
			lock (_lock)
			{
				_reading.Remove(project);
			}
		}
	}

	internal async Task<ComposeHashes> ReadHashesAsync(DockerClient client, ComposeTarget target, CancellationToken cancellationToken)
	{
		string tool = target.Tool.Display;
		DateTimeOffset readAt = _time.GetUtcNow();
		if (!target.HasFiles)
		{
			return ComposeHashes.Failed("The project's containers name no folder or compose file, so there are no files to compare with.", readAt, tool);
		}

		DockerOutput output;
		try
		{
			output = await client.RunAsync(ComposeCommands.ConfigHashes(target), cancellationToken);
		}
		catch (DockerCommandException ex)
		{
			return ComposeHashes.Failed(ex.Message, readAt, tool);
		}

		IReadOnlyDictionary<string, string> services = ComposeHashes.ParseLines(output.Output);
		if (!output.Succeeded || services.Count == 0)
		{
			string said = Complaint(output.Error.Length > 0 ? output.Error : output.Output);
			return ComposeHashes.Failed(
				said.Length > 0 ? $"{target.Tool.Command} config --hash said: {said}" : $"{target.Tool.Command} config --hash printed no hashes.",
				readAt,
				tool);
		}

		IReadOnlyList<string> unset = ComposeEnvironment.UnsetVariables(output.Error + "\n" + output.Output);
		return new ComposeHashes
		{
			Services = services,
			Uncertain = unset.Count == 0 ? new Dictionary<string, string>(StringComparer.Ordinal) : await UncertainAsync(client, target, services, unset, cancellationToken),
			ReadAt = readAt,
			Tool = tool,
		};
	}

	// Compose prints its warnings (an unset variable, an obsolete version key) before the error that stopped it.
	internal static string Complaint(string text)
	{
		string[] lines = [.. TerminalTextFilter.Plain(text).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
		string? error = lines.LastOrDefault(line => !line.StartsWith("WARN", StringComparison.OrdinalIgnoreCase) && !line.Contains("level=warning", StringComparison.OrdinalIgnoreCase));
		return RemoteText.FirstLine(error ?? (lines.Length > 0 ? lines[^1] : ""));
	}

	// Which services a variable compose found unset feeds. Without a JSON that says, every service stays in doubt.
	private static async Task<IReadOnlyDictionary<string, string>> UncertainAsync(
		DockerClient client,
		ComposeTarget target,
		IReadOnlyDictionary<string, string> services,
		IReadOnlyList<string> unset,
		CancellationToken cancellationToken)
	{
		Dictionary<string, string> everyService = services.Keys.ToDictionary(service => service, service => ComposeHashes.Uses(service, unset), StringComparer.Ordinal);
		if (!target.Tool.MaySupportConfigJson)
		{
			return everyService;
		}

		try
		{
			DockerOutput raw = await client.RunAsync(ComposeCommands.ConfigUninterpolated(target), cancellationToken);
			return raw.Succeeded && ComposeHashes.HasServices(raw.Output) ? ComposeHashes.ServicesUsing(raw.Output, unset) : everyService;
		}
		catch (DockerCommandException)
		{
			return everyService;
		}
	}
}
