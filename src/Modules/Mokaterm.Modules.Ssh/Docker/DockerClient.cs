using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The Docker engine on the session's server, through whichever shell the session uses for it. Listings are parsed
/// here and a failed one throws; a change returns its <see cref="DockerOutput"/> whatever it exited with, because the
/// page shows that output either way.
/// </summary>
/// <remarks>
/// Every command is an exec channel on the session's connection, and the connection's
/// <see cref="Shell.ExecChannelBudget"/> is what bounds how many run at once, the system view's and sudo's included.
/// </remarks>
internal sealed class DockerClient
{
	public DockerClient(IDockerShell shell, string host)
	{
		ArgumentNullException.ThrowIfNull(shell);
		ArgumentNullException.ThrowIfNull(host);
		Shell = shell;
		Host = host;
	}

	public IDockerShell Shell { get; }

	/// <summary>The server, for messages.</summary>
	public string Host { get; }

	public bool IsElevated => Shell.IsElevated;

	/// <summary>Runs a call and returns how it ended, whatever its exit.</summary>
	/// <exception cref="DockerCommandException">The command did not run to its end.</exception>
	public Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(call);
		return Shell.RunAsync(call, cancellationToken);
	}

	/// <summary>Runs a call that must succeed and returns its stdout.</summary>
	/// <exception cref="DockerCommandException">It did not run, or exited with anything but 0.</exception>
	public async Task<string> ReadAsync(DockerCall call, CancellationToken cancellationToken)
	{
		DockerOutput output = await RunAsync(call, cancellationToken);
		return output.Succeeded ? output.Output : throw Failed(output);
	}

	/// <summary>The exception for a command that ran and failed, with a sentence from its stderr.</summary>
	public DockerCommandException Failed(DockerOutput output)
	{
		ArgumentNullException.ThrowIfNull(output);
		DockerFailure failure = DockerErrors.Classify(output.ExitCode, output.Error);
		string message = DockerErrors.Describe(failure, Host, output.Error.Length > 0 ? output.Error : output.Output);
		return new DockerCommandException(failure, message, output);
	}

	public async Task<IReadOnlyList<DockerContainer>> ListContainersAsync(CancellationToken cancellationToken) =>
		DockerContainer.Parse(await ReadAsync(DockerCommands.ListContainers(), cancellationToken));

	public async Task<DockerContainerDetails> InspectAsync(string container, CancellationToken cancellationToken) =>
		DockerInspectParser.Parse(await ReadAsync(DockerCommands.Inspect(container), cancellationToken));

	public async Task<IReadOnlyList<DockerContainerStats>> StatsAsync(IReadOnlyList<string> containers, CancellationToken cancellationToken) =>
		DockerContainerStats.Parse(await ReadAsync(DockerCommands.Stats(containers), cancellationToken));

	/// <summary>The end of a container's log, both streams in order.</summary>
	public async Task<IReadOnlyList<LogLine>> LogsAsync(string container, int tail, string? since, CancellationToken cancellationToken)
	{
		DockerOutput output = await RunAsync(DockerCommands.Logs(container, tail, since), cancellationToken);
		return output.Succeeded ? DockerLogParser.Merge(output.Output, output.Error) : throw Failed(output);
	}

	/// <summary><c>bash</c> when the container has it, otherwise <c>sh</c>.</summary>
	/// <exception cref="DockerCommandException">The container cannot run anything (it is not running, or has no shell).</exception>
	public async Task<string> FindShellAsync(string container, CancellationToken cancellationToken)
	{
		DockerOutput output = await RunAsync(DockerCommands.FindShell(container), cancellationToken);
		if (output.Succeeded && output.Output.Trim().Length > 0)
		{
			return "bash";
		}

		// "command -v" exits 1 when bash is missing; anything else means sh itself did not run.
		return output.ExitCode == 1 && output.Error.Trim().Length == 0 ? "sh" : throw Failed(output);
	}

	public async Task<IReadOnlyList<DockerImage>> ListImagesAsync(CancellationToken cancellationToken) =>
		DockerImage.Parse(await ReadAsync(DockerCommands.ListImages(), cancellationToken));

	public async Task<IReadOnlyList<DockerVolume>> ListVolumesAsync(CancellationToken cancellationToken)
	{
		Task<string> listing = ReadAsync(DockerCommands.ListVolumes(), cancellationToken);
		Task<string> unused = ReadAsync(DockerCommands.UnusedVolumes(), cancellationToken);
		await Task.WhenAll(listing, unused);
		return DockerVolume.Parse(await listing, await unused);
	}

	public async Task<IReadOnlyList<DockerNetwork>> ListNetworksAsync(IReadOnlyList<DockerContainer> containers, CancellationToken cancellationToken) =>
		DockerNetwork.Parse(await ReadAsync(DockerCommands.ListNetworks(), cancellationToken), containers);

	public async Task<IReadOnlyList<DockerDiskUsage>> DiskUsageAsync(CancellationToken cancellationToken) =>
		DockerDiskUsage.Parse(await ReadAsync(DockerCommands.DiskUsage(), cancellationToken));

	/// <summary>Every image, container, volume and build cache entry with its size; null when this docker prints no such detail.</summary>
	public async Task<DockerSystemUsage?> SystemUsageAsync(CancellationToken cancellationToken) =>
		DockerSystemUsage.Parse(await ReadAsync(DockerCommands.SystemUsage(), cancellationToken));

	/// <summary>Every container with its restart count and health, for the health watch.</summary>
	public async Task<IReadOnlyList<DockerContainerState>> ContainerStatesAsync(CancellationToken cancellationToken)
	{
		DockerOutput output = await RunAsync(DockerCommands.ContainerStates(), cancellationToken);
		IReadOnlyList<DockerContainerState> states = DockerContainerState.Parse(output.Output);

		// A container removed between the listing and the inspect fails the inspect after it printed the rest.
		return output.Succeeded || states.Count > 0 ? states : throw Failed(output);
	}

	/// <summary>
	/// Every compose project: from <c>compose ls</c> where the compose has it, completed and extended from the labels
	/// of <paramref name="containers"/>.
	/// </summary>
	public async Task<IReadOnlyList<ComposeProject>> ListComposeProjectsAsync(
		ComposeTool? tool,
		IReadOnlyList<DockerContainer> containers,
		CancellationToken cancellationToken)
	{
		string? listing = tool is { SupportsList: true } ? await ReadAsync(ComposeCommands.List(tool), cancellationToken) : null;
		return ComposeProject.Parse(listing, containers);
	}

	/// <summary>Which of <paramref name="paths"/> exist as files on the docker host, in one command.</summary>
	public async Task<IReadOnlyList<bool>> FileStatesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(paths);
		if (paths.Count == 0)
		{
			return [];
		}

		string output = await ReadAsync(DockerHostScripts.FileStates(paths), cancellationToken);
		return DockerHostScripts.ParseFileStates(output, paths.Count);
	}

	/// <summary>Compose files under the usual roots, whether or not a project runs from them.</summary>
	public async Task<IReadOnlyList<string>> FindComposeFilesAsync(CancellationToken cancellationToken) =>
		DockerHostScripts.ParseFoundFiles(await ReadAsync(DockerHostScripts.FindComposeFiles(), cancellationToken));

	/// <summary>
	/// The env files the services name, from <c>config --format json</c>; null when the compose cannot print JSON, so
	/// the caller reads the YAML itself.
	/// </summary>
	public async Task<IReadOnlyList<string>?> ComposeEnvFilesAsync(ComposeTarget target, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (!target.Tool.MaySupportConfigJson || !target.HasFiles)
		{
			return null;
		}

		DockerOutput output = await RunAsync(ComposeCommands.ConfigJson(target), cancellationToken);
		return output.Succeeded ? ComposeEnvironment.EnvFilesFromConfigJson(output.Output) : null;
	}

	/// <summary>The variables the files use and nothing sets, from the warnings <c>config --quiet</c> prints.</summary>
	public async Task<IReadOnlyList<string>> UnsetVariablesAsync(ComposeTarget target, CancellationToken cancellationToken)
	{
		DockerOutput output = await RunAsync(ComposeCommands.ConfigCheck(target), cancellationToken);
		return ComposeEnvironment.UnsetVariables(output.Error + "\n" + output.Output);
	}

	/// <summary>A volume with the containers that mount it and where.</summary>
	public async Task<DockerVolumeDetails> VolumeDetailsAsync(string name, CancellationToken cancellationToken)
	{
		DockerVolumeDetails volume = DockerVolumeDetails.Parse(await ReadAsync(DockerCommands.InspectVolume(name), cancellationToken))
			?? throw new DockerCommandException(DockerFailure.UnreadableOutput, "docker volume inspect printed no volume.");

		IReadOnlyList<DockerContainerRow> users = DockerContainerRow.Parse(
			await ReadAsync(DockerCommands.ContainersUsingVolume(name), cancellationToken));
		if (users.Count == 0)
		{
			return volume;
		}

		IReadOnlyList<DockerContainerDetails> details = DockerInspectParser.ParseAll(
			await ReadAsync(DockerCommands.InspectAll([.. users.Select(user => user.Id)]), cancellationToken));
		return volume with { UsedBy = DockerVolumeUse.Find(details, volume.Name) };
	}

	public async Task<IReadOnlyList<ComposeContainer>> ComposeContainersAsync(ComposeTarget target, CancellationToken cancellationToken) =>
		ComposeContainer.Parse(await ReadAsync(ComposeCommands.Containers(target), cancellationToken));

	/// <summary>The services the files define, in their order.</summary>
	public async Task<IReadOnlyList<string>> ComposeServicesAsync(ComposeTarget target, CancellationToken cancellationToken)
	{
		string output = await ReadAsync(ComposeCommands.Services(target), cancellationToken);
		return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	public Task<string> ComposeConfigAsync(ComposeTarget target, CancellationToken cancellationToken) =>
		ReadAsync(ComposeCommands.Config(target), cancellationToken);

	/// <summary>The project's merged log, every line with the container it came from, oldest first.</summary>
	public async Task<IReadOnlyList<LogLine>> ComposeLogsAsync(ComposeTarget target, string? service, int tail, string? since, CancellationToken cancellationToken)
	{
		DockerOutput output = await RunAsync(ComposeCommands.Logs(target, service, tail, since), cancellationToken);
		return output.Succeeded ? DockerLogParser.MergeCompose(output.Output, output.Error) : throw Failed(output);
	}

	/// <summary>Runs a command inside a container and returns how it ended, whatever it exited with.</summary>
	public Task<DockerOutput> ExecAsync(DockerExecRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return RunAsync(request.ToCall(), cancellationToken);
	}
}
