using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A project's env files: the <c>.env</c> in its folder (or the files it was started with through <c>--env-file</c>),
/// every <c>env_file:</c> its services name, which variables the compose files use that nothing sets, and an editor
/// for each file. The file browser's own editor cannot be reached from a module, so the editor is here and saves the
/// way that editor does: the session file system's replace, which keeps the file's owner and mode, refusing a file
/// that changed since it was opened. Values stay masked until shown one at a time, and none is ever logged.
/// </summary>
public sealed partial class ComposeEnvPanel : DockerPageBase
{
	private IReadOnlyList<EnvFileView> _files = [];
	private IReadOnlyList<string> _unset = [];
	private string? _loadedFor;
	private bool _asRoot;
	private EnvFileView? _editing;
	private string _draft = "";
	private bool _saving;
	private string? _saveProblem;

	[CascadingParameter]
	private ComposeProjectContext? Context { get; set; }

	private bool CanUseRoot => HostFiles?.CanUseRoot ?? false;

	protected override Task OnParametersSetAsync() =>
		Context is { } context && context.Project.Name != _loadedFor ? LoadAllAsync() : Task.CompletedTask;

	private static string SaveNote(HostTextFile file) =>
		file.ReadAsRoot
			? "Saves as root through sudo, keeping the file's owner and mode. Containers see the change on their next up."
			: "Saves over the file, keeping its owner and mode. Containers see the change on their next up.";

	private Task UseRootAsync()
	{
		_asRoot = true;
		return LoadAllAsync();
	}

	/// <summary>Works out which files feed the project, which exist, what they hold, and what is used but unset.</summary>
	private Task LoadAllAsync() => LoadAsync(async (client, token) =>
	{
		if (Context is not { } context || HostFiles is not { } files)
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
				HostTextFile yaml = await files.ReadTextAsync(composeFile, _asRoot, token);
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
			view.Text = await files.ReadTextAsync(view.Path, _asRoot, token);
			view.Variables = DotEnvParser.Parse(view.Text.Text);
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

	private void Edit(EnvFileView file)
	{
		if (file.Text is not { Truncated: false } text)
		{
			return;
		}

		_editing = file;
		_draft = text.Text;
		_saveProblem = null;
	}

	private void OnDraftChanged(string? value) => _draft = value ?? "";

	private void CancelEdit()
	{
		_editing = null;
		_draft = "";
		_saveProblem = null;
	}

	private async Task SaveAsync()
	{
		if (_editing is not { Text: { } original } file || HostFiles is not { } files || _saving)
		{
			return;
		}

		_saving = true;
		_saveProblem = null;
		try
		{
			await files.WriteTextAsync(original, _draft, Lifetime);
			await ReadAsync(files, file, Lifetime);
			_editing = null;
			_draft = "";
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_saveProblem = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_saving = false;
		}

		StateHasChanged();
	}

	/// <summary>One env file as the tab shows it; mutable, since reading and saving fill it in place.</summary>
	private sealed class EnvFileView(string path, string role, bool required, bool exists)
	{
		public string Path { get; } = path;

		/// <summary>Where it comes from: the project's <c>.env</c>, <c>--env-file</c> or a service's <c>env_file</c>.</summary>
		public string Role { get; } = role;

		/// <summary>False for the default <c>.env</c>, which compose does without when it is not there.</summary>
		public bool Required { get; } = required;

		public bool Exists { get; } = exists;

		public HostTextFile? Text { get; set; }

		public IReadOnlyList<DotEnvVariable> Variables { get; set; } = [];

		public string? Problem { get; set; }

		public bool NeedsRoot { get; set; }
	}
}
