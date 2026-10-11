using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A compose project: what <c>compose ls -a</c> printed for it, completed from the labels compose puts on every
/// container, which is also the only source where the compose has no <c>ls</c> (the Python 1.x). Every path is
/// absolute: compose 1.x labels its files as they were typed, so they are resolved against the project's folder.
/// </summary>
internal sealed record ComposeProject
{
	public required string Name { get; init; }

	/// <summary>As compose prints it, for example <c>running(2), exited(1)</c>.</summary>
	public string Status { get; init; } = "";

	/// <summary>Absolute paths of the compose files, in the order compose read them.</summary>
	public IReadOnlyList<string> ConfigFiles { get; init; } = [];

	/// <summary>The project's folder: where compose ran from, from the containers' labels, else the first file's folder.</summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>
	/// The env files the project was started with (<c>--env-file</c>), which recent composes record in a label. Empty
	/// means the default: <c>.env</c> in the project's folder.
	/// </summary>
	public IReadOnlyList<string> EnvironmentFiles { get; init; } = [];

	/// <summary>The project's containers from the container list, one-off <c>compose run</c> containers left out.</summary>
	public IReadOnlyList<DockerContainer> Containers { get; init; } = [];

	public int RunningCount => Containers.Count(container => container.IsRunning);

	/// <summary>The project's one state from what its containers' labels and states say.</summary>
	public ComposeProjectHealth Health => ComposeService.HealthOf(ComposeService.Group([.. Containers.Select(ComposeContainer.FromContainer)]));

	public bool Matches(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		string needle = query.Trim();
		return needle.Length == 0
			|| Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
			|| (WorkingDirectory?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
			|| Containers.Any(container => container.ComposeService?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	/// <summary>
	/// The projects in <paramref name="listing"/> (the output of <c>compose ls -a --format json</c>, or null without one)
	/// plus every project only the containers' labels name, sorted by name.
	/// </summary>
	public static IReadOnlyList<ComposeProject> Parse(string? listing, IReadOnlyList<DockerContainer> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		Dictionary<string, List<DockerContainer>> members = new(StringComparer.Ordinal);
		foreach (DockerContainer container in containers)
		{
			if (container.ComposeProject is { } project && !container.IsOneOff)
			{
				if (!members.TryGetValue(project, out List<DockerContainer>? list))
				{
					list = [];
					members[project] = list;
				}

				list.Add(container);
			}
		}

		Dictionary<string, ComposeProject> projects = new(StringComparer.Ordinal);
		foreach (JsonElement item in DockerJsonOutput.ReadObjects(listing))
		{
			string name = DockerJson.Text(item, "Name");
			if (name.Length == 0)
			{
				continue;
			}

			List<DockerContainer> own = members.TryGetValue(name, out List<DockerContainer>? found) ? found : [];
			IReadOnlyList<string> listed = DockerLabels.SplitList(DockerJson.Text(item, "ConfigFiles"));
			projects[name] = Build(name, DockerJson.Text(item, "Status"), listed, own);
		}

		foreach ((string name, List<DockerContainer> own) in members)
		{
			if (!projects.ContainsKey(name))
			{
				projects[name] = Build(name, StatusOf(own), [], own);
			}
		}

		return [.. projects.Values.OrderBy(project => project.Name, StringComparer.Ordinal)];
	}

	/// <summary>The way compose itself summarises a project: <c>running(2), exited(1)</c>.</summary>
	public static string StatusOf(IReadOnlyList<DockerContainer> containers)
	{
		ArgumentNullException.ThrowIfNull(containers);
		return string.Join(", ", containers
			.GroupBy(container => container.State.Length == 0 ? "unknown" : container.State, StringComparer.Ordinal)
			.OrderBy(group => group.Key, StringComparer.Ordinal)
			.Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}({group.Count()})")));
	}

	private static ComposeProject Build(string name, string status, IReadOnlyList<string> listed, List<DockerContainer> own)
	{
		string? labelledFolder = own.Select(container => container.ComposeWorkingDirectory).FirstOrDefault(folder => folder is { Length: > 0 });
		IReadOnlyList<string> raw = listed.Count > 0 ? listed : FirstList(own, container => container.ComposeConfigFiles);

		// The folder resolves the files and the first file names the folder, so a project with absolute files and no
		// folder label still gets one, and one with relative files and a folder label gets absolute files.
		string? folder = labelledFolder ?? raw.Select(file => file.StartsWith('/') ? DockerHostPaths.DirectoryOf(file) : null).FirstOrDefault(found => found is not null);
		List<string> files = [.. raw.Select(file => DockerHostPaths.Resolve(folder, file)).OfType<string>()];
		List<string> environment = [.. FirstList(own, container => container.ComposeEnvironmentFiles).Select(file => DockerHostPaths.Resolve(folder, file)).OfType<string>()];
		return new ComposeProject
		{
			Name = name,
			Status = status,
			ConfigFiles = files,
			WorkingDirectory = folder,
			EnvironmentFiles = environment,
			Containers = own,
		};
	}

	private static IReadOnlyList<string> FirstList(List<DockerContainer> containers, Func<DockerContainer, IReadOnlyList<string>> read) =>
		containers.Select(read).FirstOrDefault(files => files.Count > 0) ?? [];
}
