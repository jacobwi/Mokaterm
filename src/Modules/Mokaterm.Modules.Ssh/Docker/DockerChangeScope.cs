namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The containers a change the user ran may stop, restart or recreate: the ones a container command names, every
/// container of a compose project, nothing for a change that only adds or moves images or credentials (a pull, a new
/// container, a build, a tag, a push, a registry login), or everything for a
/// command this cannot read. The health watch says nothing about what a change of the user's own touched.
/// </summary>
internal sealed record DockerChangeScope
{
	private static readonly HashSet<string> ContainerCommands = new(StringComparer.Ordinal) { "start", "stop", "restart", "kill", "pause", "unpause", "rm" };

	private static readonly HashSet<string> TouchesNone = new(StringComparer.Ordinal)
	{
		"run", "create", "pull", "image", "volume", "network", "builder", "system", "build", "buildx", "tag", "push", "login", "logout",
	};

	// Compose commands that make or move images and never stop, start or recreate a container.
	private static readonly HashSet<string> ComposeTouchesNone = new(StringComparer.Ordinal) { "build", "push" };

	// The compose options that come before the command, each followed by its value.
	private static readonly HashSet<string> ComposeOptions = new(StringComparer.Ordinal) { "-p", "-f", "--project-directory", "--env-file" };

	public static readonly DockerChangeScope Nothing = new();

	public static readonly DockerChangeScope Everything = new() { All = true };

	public bool All { get; init; }

	/// <summary>Names or ids, as the command names them.</summary>
	public IReadOnlySet<string> Containers { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	public IReadOnlySet<string> Projects { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	public static DockerChangeScope Of(IEnumerable<DockerCall> calls)
	{
		ArgumentNullException.ThrowIfNull(calls);
		HashSet<string> containers = new(StringComparer.Ordinal);
		HashSet<string> projects = new(StringComparer.Ordinal);
		foreach (DockerCall call in calls)
		{
			DockerChangeScope scope = Of(call);
			if (scope.All)
			{
				return Everything;
			}

			containers.UnionWith(scope.Containers);
			projects.UnionWith(scope.Projects);
		}

		return new DockerChangeScope { Containers = containers, Projects = projects };
	}

	public static DockerChangeScope Of(DockerCall call)
	{
		ArgumentNullException.ThrowIfNull(call);

		// A build or a push runs under the server's own time limit; what it touches is what the command inside touches.
		if (DockerTimeLimit.Unwrap(call) is { } inner)
		{
			return Of(inner);
		}

		IReadOnlyList<string> words = call.Arguments;
		switch (call.Program)
		{
			case DockerProgram.Docker when words.Count > 0 && ContainerCommands.Contains(words[0]):
				return new DockerChangeScope { Containers = new HashSet<string>(words.Skip(1).Where(word => !word.StartsWith('-')), StringComparer.Ordinal) };
			case DockerProgram.Docker when words.Count > 0 && TouchesNone.Contains(words[0]):
				return Nothing;
			case DockerProgram.ComposePlugin or DockerProgram.ComposeStandalone when ComposeCommand(words) is { } command && ComposeTouchesNone.Contains(command):
				return Nothing;
			case DockerProgram.ComposePlugin or DockerProgram.ComposeStandalone:
				int project = IndexOf(words, "-p");
				return project >= 0 && project + 1 < words.Count
					? new DockerChangeScope { Projects = new HashSet<string>([words[project + 1]], StringComparer.Ordinal) }
					: Everything;
			default:
				return Everything;
		}
	}

	public bool Covers(DockerContainerState container)
	{
		ArgumentNullException.ThrowIfNull(container);
		return All
			|| Containers.Contains(container.Name)
			|| Containers.Any(reference => reference.Length >= 12 && container.Id.StartsWith(reference, StringComparison.Ordinal))
			|| (container.ComposeProject is { } project && Projects.Contains(project));
	}

	/// <summary>The compose command after the options <see cref="ComposeCommands"/> puts first, or null when there is none.</summary>
	private static string? ComposeCommand(IReadOnlyList<string> words)
	{
		int index = 0;
		while (index < words.Count && ComposeOptions.Contains(words[index]))
		{
			index += 2;
		}

		return index < words.Count ? words[index] : null;
	}

	private static int IndexOf(IReadOnlyList<string> words, string word)
	{
		for (int index = 0; index < words.Count; index++)
		{
			if (words[index] == word)
			{
				return index;
			}
		}

		return -1;
	}
}
