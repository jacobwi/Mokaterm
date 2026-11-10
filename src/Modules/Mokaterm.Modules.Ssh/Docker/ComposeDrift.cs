namespace Mokaterm.Modules.Ssh.Docker;

// Whether a compose project's services still run what their files define, from the files' hashes and the
// config-hash label on each container. Changed is said only where both are known and differ; anything that leaves
// the comparison in doubt is Unknown, which nothing draws as a claim.
internal sealed record ComposeDrift(IReadOnlyList<ComposeServiceDrift> Services, string? Problem, DateTimeOffset ReadAt)
{
	public IReadOnlyList<ComposeServiceDrift> Changed => [.. Services.Where(service => service.State == ComposeDriftState.Changed)];

	public ComposeDriftState State =>
		Services.Any(service => service.State == ComposeDriftState.Changed) ? ComposeDriftState.Changed
		: Services.Count > 0 && Services.All(service => service.State == ComposeDriftState.Current) ? ComposeDriftState.Current
		: ComposeDriftState.Unknown;

	public IReadOnlyList<ComposeServiceDrift> Unknown => [.. Services.Where(service => service.State == ComposeDriftState.Unknown)];

	// What can and cannot be told, for a project that has no changed service.
	public string? UnknownText()
	{
		if (Problem is { } problem)
		{
			return $"Whether the files changed since the last up cannot be told. {problem}";
		}

		IReadOnlyList<ComposeServiceDrift> unknown = Unknown;
		if (unknown.Count == 0)
		{
			return null;
		}

		string[] current = [.. Services.Where(service => service.State == ComposeDriftState.Current).Select(service => service.Service)];
		string doubt = $"Whether {string.Join(", ", unknown.Select(service => service.Service))} changed cannot be told: {unknown[0].Detail}";
		string verb = current.Length == 1 ? "runs" : "run";
		return current.Length == 0 ? doubt : $"{string.Join(", ", current)} {verb} what the files define. {doubt}";
	}

	public ComposeServiceDrift? Find(string service) => Services.FirstOrDefault(item => item.Service == service);

	public string ChangedText()
	{
		IReadOnlyList<ComposeServiceDrift> changed = Changed;
		string names = string.Join(", ", changed.Select(service => service.Service));
		string text = changed.Count == 1
			? $"The compose files no longer define {names} the way it was brought up. Up makes it again."
			: $"The compose files no longer define {names} the way they were brought up. Up makes them again.";
		IReadOnlyList<ComposeServiceDrift> unknown = Unknown;
		return unknown.Count == 0 ? text : $"{text} Whether {string.Join(", ", unknown.Select(service => service.Service))} changed cannot be told.";
	}

	public static ComposeDrift? Compare(ComposeHashes? hashes, IReadOnlyList<DockerContainer> containers, ComposeTool? tool)
	{
		ArgumentNullException.ThrowIfNull(containers);
		if (hashes is null || tool is null || hashes.Tool != tool.Display)
		{
			return null;
		}

		List<ComposeServiceDrift> services = [];
		foreach (IGrouping<string, DockerContainer> service in containers
			.Where(container => !container.IsOneOff && container.ComposeService is not null)
			.GroupBy(container => container.ComposeService!, StringComparer.Ordinal)
			.OrderBy(group => group.Key, StringComparer.Ordinal))
		{
			services.Add(Service(service.Key, [.. service], hashes, tool));
		}

		return new ComposeDrift(services, hashes.Problem, hashes.ReadAt);
	}

	private static ComposeServiceDrift Service(string name, IReadOnlyList<DockerContainer> containers, ComposeHashes hashes, ComposeTool tool)
	{
		if (hashes.Problem is { } problem)
		{
			return new ComposeServiceDrift(name, ComposeDriftState.Unknown, problem);
		}

		if (!hashes.Services.TryGetValue(name, out string? expected))
		{
			return new ComposeServiceDrift(name, ComposeDriftState.Unknown, "The compose files as they read now have no such service, or it sits behind a profile.");
		}

		if (hashes.Uncertain.TryGetValue(name, out string? uncertain))
		{
			return new ComposeServiceDrift(name, ComposeDriftState.Unknown, uncertain);
		}

		string? unknown = null;
		foreach (DockerContainer container in containers)
		{
			string? label = Label(container, DockerLabels.ComposeConfigHash);
			string? version = Label(container, DockerLabels.ComposeVersion);
			int major = version is null ? 0 : new ComposeTool(tool.Program, version).MajorVersion;
			if (label is null)
			{
				unknown ??= $"{container.Name} carries no config-hash label to compare with.";
			}
			else if (major > 0 && tool.MajorVersion > 0 && major != tool.MajorVersion)
			{
				unknown ??= $"{container.Name} was made by compose {version}, which works its hash out differently from {tool.Version}.";
			}
			else if (!string.Equals(label, expected, StringComparison.Ordinal))
			{
				return new ComposeServiceDrift(name, ComposeDriftState.Changed, $"{container.Name} was made from a different definition than the files hold now: up makes it again.");
			}
		}

		return unknown is null
			? new ComposeServiceDrift(name, ComposeDriftState.Current, "Runs what the files define.")
			: new ComposeServiceDrift(name, ComposeDriftState.Unknown, unknown);
	}

	private static string? Label(DockerContainer container, string key) =>
		container.Labels.TryGetValue(key, out string? value) && value.Trim().Length > 0 ? value.Trim() : null;
}
