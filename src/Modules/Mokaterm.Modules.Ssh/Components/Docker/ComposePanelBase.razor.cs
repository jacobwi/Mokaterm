using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Editing;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What the tabs of a compose project share: the project, cascaded by the project view, and editing the project's own
/// files with the editor the file browser uses. Once a save went through, compose reads the files again, so a file that
/// no longer parses shows at once instead of at the next up, and the project's own Up is offered to put the change to
/// use. A file the login may not open is offered as root, through the session's one root file system.
/// </summary>
public abstract partial class ComposePanelBase : DockerManagerPageBase
{
	[CascadingParameter]
	private protected ComposeProjectContext? ProjectContext { get; set; }

	[Inject]
	private IRemoteTextEditor Editor { get; set; } = default!;

	/// <summary>What the last edit came to, kept on the tab until dismissed or the next edit.</summary>
	private protected ProjectFileEdit? LastEdit { get; private set; }

	/// <summary>True from opening a file until the editor closes and compose has had its look.</summary>
	private protected bool IsEditing { get; private set; }

	/// <summary>
	/// Edits <paramref name="path"/> on the docker host, or with <paramref name="createNew"/> creates it empty first,
	/// keeps what came of it in <see cref="LastEdit"/>, and once a file was saved or created lets the tab read what it
	/// shows again (<see cref="OnProjectFileSavedAsync"/>).
	/// </summary>
	private protected async Task EditProjectFileAsync(string path, bool asRoot, bool createNew = false)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (IsEditing || HostFiles is not { } files)
		{
			return;
		}

		if (await EditAsync(files, path, asRoot, createNew) || createNew)
		{
			await OnProjectFileSavedAsync();
		}
	}

	/// <summary>Reads again what the tab shows of a file that was just saved or created.</summary>
	private protected virtual Task OnProjectFileSavedAsync() => Task.CompletedTask;

	/// <returns>True once a save went through.</returns>
	private async Task<bool> EditAsync(DockerHostFiles files, string path, bool asRoot, bool createNew)
	{
		IsEditing = true;
		LastEdit = null;
		try
		{
			RemoteTextEditResult result;
			try
			{
				IRemoteFileSystem fileSystem = await files.OpenAsync(asRoot, Lifetime);
				result = await Editor.EditAsync(new RemoteTextEditRequest { FileSystem = fileSystem, Path = path, CreateNew = createNew }, Lifetime);
			}
			catch (Exception ex) when (Describe(ex) is { } problem)
			{
				result = RemoteTextEditResult.Failed(ex, problem);
			}

			switch (result.Outcome)
			{
				case RemoteTextEditOutcome.Failed:
					bool offerRoot = result.IsPermissionDenied && !asRoot && CanUseRoot;
					LastEdit = ProjectFileEdit.Failure(path, asRoot, createNew, result.Message ?? "It could not be opened.", offerRoot);
					return false;
				case RemoteTextEditOutcome.Saved:
					LastEdit = await CheckAsync(path, asRoot);
					return true;
				default:
					return false;
			}
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		finally
		{
			IsEditing = false;
		}
	}

	/// <summary>Runs the project's Up, which is what puts a saved file to use, and lets go of the note that offered it.</summary>
	private protected async Task UpAsync()
	{
		LastEdit = null;
		if (ProjectContext is { } context)
		{
			await context.RunAction("up");
		}
	}

	private protected void DismissEdit() => LastEdit = null;

	/// <summary>Has compose read the project's files again, the way <c>config</c> on the Config tab does.</summary>
	private async Task<ProjectFileEdit> CheckAsync(string path, bool asRoot)
	{
		if (ProjectContext?.Target is not { HasFiles: true } target || Client is not { } client)
		{
			return ProjectFileEdit.Written(path, asRoot, checkedByCompose: false);
		}

		try
		{
			_ = await client.ComposeConfigAsync(target, Lifetime);
			return ProjectFileEdit.Written(path, asRoot, checkedByCompose: true);
		}
		catch (DockerCommandException ex)
		{
			string? output = ex.Output is { } printed ? TerminalTextFilter.Plain(printed.Error).Trim() : null;
			return ProjectFileEdit.Refused(path, asRoot, ex.Message, output);
		}
	}
}
