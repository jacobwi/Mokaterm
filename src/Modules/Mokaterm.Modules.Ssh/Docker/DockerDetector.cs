namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Looks for Docker on the server: whether the client exists and the daemon answers (<c>docker version</c>), then what
/// the daemon is (<c>docker info</c>, which also says whether it runs rootless) and which compose there is. The last
/// three run side by side, and only <c>docker version</c> decides whether the manager can be used.
/// </summary>
internal static class DockerDetector
{
	public static async Task<DockerEngineStatus> DetectAsync(DockerClient client, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		DockerOutput versionOutput;
		try
		{
			versionOutput = await client.RunAsync(DockerCommands.Version(), cancellationToken);
		}
		catch (DockerCommandException ex)
		{
			return new DockerEngineStatus { Availability = DockerAvailability.Failed, Message = ex.Message, IsElevated = client.IsElevated };
		}

		DockerVersionInfo? version = DockerVersionInfo.Parse(versionOutput.Output);
		if (versionOutput.Succeeded && version is { HasServer: true })
		{
			Task<DockerEngineInfo?> info = ReadInfoAsync(client, cancellationToken);
			Task<ComposeDetection> compose = FindComposeAsync(client, cancellationToken);
			await Task.WhenAll(info, compose);
			return new DockerEngineStatus
			{
				Availability = DockerAvailability.Ready,
				Version = version,
				Info = await info,
				Compose = await compose,
				IsElevated = client.IsElevated,
			};
		}

		DockerFailure failure = DockerErrors.Classify(versionOutput.ExitCode, versionOutput.Error);
		DockerAvailability availability = failure switch
		{
			DockerFailure.NotInstalled => DockerAvailability.NotInstalled,
			DockerFailure.PermissionDenied => DockerAvailability.PermissionDenied,
			DockerFailure.DaemonUnreachable => DockerAvailability.DaemonUnreachable,

			// A client that printed itself but no server reached no daemon, whatever words it used for that.
			_ when version is not null => DockerAvailability.DaemonUnreachable,
			_ => DockerAvailability.Failed,
		};

		DockerFailure described = availability == DockerAvailability.DaemonUnreachable ? DockerFailure.DaemonUnreachable : failure;
		return new DockerEngineStatus
		{
			Availability = availability,
			Message = DockerErrors.Describe(described, client.Host, versionOutput.Error.Length > 0 ? versionOutput.Error : versionOutput.Output),
			Version = version,
			IsElevated = client.IsElevated,
		};
	}

	/// <summary>The info object, or null when it cannot be had; the overview then shows less.</summary>
	private static async Task<DockerEngineInfo?> ReadInfoAsync(DockerClient client, CancellationToken cancellationToken)
	{
		try
		{
			DockerOutput output = await client.RunAsync(DockerCommands.Info(), cancellationToken);
			return DockerEngineInfo.Parse(output.Output);
		}
		catch (DockerCommandException)
		{
			return null;
		}
	}

	/// <summary>
	/// Both composes, side by side. A standalone docker-compose too old for <c>version --short</c> is asked again with
	/// <c>-v</c>; one the shell cannot find at all (exit 127) is not asked twice.
	/// </summary>
	internal static async Task<ComposeDetection> FindComposeAsync(DockerClient client, CancellationToken cancellationToken)
	{
		Task<string?> plugin = PluginVersionAsync(client, cancellationToken);
		Task<string?> standalone = StandaloneVersionAsync(client, cancellationToken);
		await Task.WhenAll(plugin, standalone);
		return new ComposeDetection(
			await plugin is { } pluginVersion ? new ComposeTool(DockerProgram.ComposePlugin, pluginVersion) : null,
			await standalone is { } standaloneVersion ? new ComposeTool(DockerProgram.ComposeStandalone, standaloneVersion) : null);
	}

	private static async Task<string?> PluginVersionAsync(DockerClient client, CancellationToken cancellationToken) =>
		await VersionOfAsync(client, DockerCommands.ComposePluginVersion(), cancellationToken) is { Version: { } version } ? version : null;

	private static async Task<string?> StandaloneVersionAsync(DockerClient client, CancellationToken cancellationToken)
	{
		(string? version, int? exit) = await VersionOfAsync(client, DockerCommands.ComposeStandaloneVersion(), cancellationToken);
		if (version is not null || exit is null or DockerErrors.CommandNotFoundExitStatus)
		{
			return version;
		}

		return (await VersionOfAsync(client, DockerCommands.ComposeStandaloneVersionLong(), cancellationToken)).Version;
	}

	private static async Task<(string? Version, int? Exit)> VersionOfAsync(DockerClient client, DockerCall call, CancellationToken cancellationToken)
	{
		try
		{
			DockerOutput output = await client.RunAsync(call, cancellationToken);
			return (output.Succeeded ? ComposeTool.ParseVersion(output.Output) : null, output.ExitCode);
		}
		catch (DockerCommandException)
		{
			return (null, null);
		}
	}
}
