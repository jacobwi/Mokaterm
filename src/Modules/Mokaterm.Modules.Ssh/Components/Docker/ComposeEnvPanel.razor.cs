using System.Globalization;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A project's env files: the <c>.env</c> in its folder (or the files it was started with through <c>--env-file</c>),
/// every <c>env_file:</c> its services name, and which variables the compose files use that nothing sets. Each file's
/// variables are listed with their values masked until shown one at a time, and none is ever logged. Editing, and
/// creating a file that is missing, goes through the editor the file browser uses (<see cref="ComposePanelBase"/>).
/// </summary>
public sealed partial class ComposeEnvPanel : ComposePanelBase
{
	private IReadOnlyList<EnvFileView> _files = [];
	private IReadOnlyList<string> _unset = [];
	private string? _loadedFor;
	private bool _asRoot;

	private static string CutText => string.Create(
		CultureInfo.CurrentCulture,
		$"Only the first {DockerHostFiles.MaxViewBytes / 1024} KB were read, so variables after that are not listed. Edit opens the whole file.");

	protected override Task OnParametersSetAsync() =>
		ProjectContext is { } context && context.Project.Name != _loadedFor ? LoadAllAsync() : Task.CompletedTask;

	/// <summary>A saved file changes its variables and maybe what is unset, so everything is read again.</summary>
	private protected override Task OnProjectFileSavedAsync() => LoadAllAsync();

	private Task UseRootAsync()
	{
		_asRoot = true;
		return LoadAllAsync();
	}

	/// <summary>Works out which files feed the project, which exist, what they hold, and what is used but unset.</summary>
	private Task LoadAllAsync() => LoadAsync(async (client, token) =>
	{
		if (ProjectContext is not { } context || HostFiles is not { } files)
		{
			return;
		}

		_loadedFor = context.Project.Name;
		List<(string Path, string Role, bool Required)> wanted = [];
		if (context.Project.EnvironmentFiles.Count > 0)
		{
			wanted.AddRange(context.Project.EnvironmentFiles.Select(path => (path, "--env-file", true)));
		}
		else if (context.Project.WorkingDirectory is { } folder)
		{
			wanted.Add((DockerHostPaths.Resolve(folder, ".env")!, "Project .env", false));
		}

		foreach (string path in await EnvFilesOfServicesAsync(client, context, files, token))
		{
			if (!wanted.Any(entry => entry.Path == path))
			{
				wanted.Add((path, "env_file", true));
			}
		}

		IReadOnlyList<bool> exists = await client.FileStatesAsync([.. wanted.Select(entry => entry.Path)], token);
		List<EnvFileView> views = [];
		for (int index = 0; index < wanted.Count; index++)
		{
			EnvFileView view = new(wanted[index].Path, wanted[index].Role, wanted[index].Required, exists[index]);
			if (view.Exists)
			{
				await ReadAsync(files, view, token);
			}

			views.Add(view);
		}

		_files = views;
		_unset = context.Target is { HasFiles: true } target ? await client.UnsetVariablesAsync(target, token) : [];
	});

	/// <summary>
	/// The <c>env_file:</c> entries: from <c>config --format json</c> where compose prints it, else read from the YAML of
	/// each compose file, which only the inline and block forms of the key are taken from.
	/// </summary>
	private async Task<IReadOnlyList<string>> EnvFilesOfServicesAsync(DockerClient client, ComposeProjectContext context, DockerHostFiles files, CancellationToken token)
	{
		if (context.Target is { } target && await client.ComposeEnvFilesAsync(target, token) is { } fromJson)
		{
			return fromJson;
		}

		List<string> found = [];
		foreach (string composeFile in context.Project.ConfigFiles)
		{
			try
			{
				RemoteTextFile yaml = await files.ReadTextAsync(composeFile, _asRoot, token);
				found.AddRange(ComposeEnvironment.EnvFilesFromYaml(yaml.Text, composeFile).Where(path => !found.Contains(path, StringComparer.Ordinal)));
			}
			catch (RemoteFileSystemException)
			{
				// A compose file that cannot be read names no env files here; the files tab says which is missing.
			}
		}

		return found;
	}

	private async Task ReadAsync(DockerHostFiles files, EnvFileView view, CancellationToken token)
	{
		try
		{
			RemoteTextFile text = await files.ReadTextAsync(view.Path, _asRoot, token);
			view.Variables = DotEnvParser.Parse(text.Text);
			view.IsCut = text.IsCut;
			view.Problem = null;
			view.NeedsRoot = false;
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.PermissionDenied && !_asRoot)
		{
			view.Problem = "The login may not read this file.";
			view.NeedsRoot = true;
		}
		catch (RemoteFileSystemException ex)
		{
			view.Problem = ex.Message;
		}
	}

	/// <summary>One env file as the tab shows it; mutable, since reading fills it in place.</summary>
	private sealed class EnvFileView(string path, string role, bool required, bool exists)
	{
		public string Path { get; } = path;

		/// <summary>Where it comes from: the project's <c>.env</c>, <c>--env-file</c> or a service's <c>env_file</c>.</summary>
		public string Role { get; } = role;

		/// <summary>False for the default <c>.env</c>, which compose does without when it is not there.</summary>
		public bool Required { get; } = required;

		public bool Exists { get; } = exists;

		public IReadOnlyList<DotEnvVariable> Variables { get; set; } = [];

		/// <summary>True when only the start of the file was read, so the list may stop short.</summary>
		public bool IsCut { get; set; }

		public string? Problem { get; set; }

		public bool NeedsRoot { get; set; }
	}
}
