using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A read-only look at a folder on the docker host (a volume's mountpoint, a bind mount's source) or at one file in it:
/// folders open, text files show, binary files are refused. It reads as the login first and offers root through the
/// module's sudo file system when the login may not, which is the usual case under <c>/var/lib/docker</c>. It never
/// writes and never walks above the folder it was opened on.
/// </summary>
public sealed partial class DockerFileViewer : DockerPageBase
{
	private string _path = "";
	private IReadOnlyList<RemoteFileEntry>? _entries;
	private HostTextFile? _file;
	private string? _problem;
	private bool _needsRoot;
	private bool _asRoot;
	private bool _busy;

	/// <summary>The absolute path the viewer starts at and never goes above.</summary>
	[Parameter, EditorRequired]
	public string Root { get; set; } = "/";

	private bool CanUseRoot => HostFiles?.CanUseRoot ?? false;

	private string RootNote => CanUseRoot
		? "Reading as root goes through sudo with the session's sudo password, which is sent on stdin and never shown."
		: "Running as root is turned off for this connection, so only what the login may read can be shown.";

	protected override Task OnInitializedAsync()
	{
		_path = Root;
		return ReloadAsync();
	}

	private static string FileLine(HostTextFile file)
	{
		string size = DisplayFormat.Bytes(file.Size);
		return file.Truncated
			? string.Create(CultureInfo.CurrentCulture, $"{size}, only the first {DockerHostFiles.MaxTextBytes / 1024} KB shown")
			: size;
	}

	private Task ReloadAsync() => ShowAsync(_path);

	private Task OpenAsync(RemoteFileEntry entry) => ShowAsync(entry.Path);

	private Task UpAsync()
	{
		string parent = DockerHostPaths.DirectoryOf(_path);
		return ShowAsync(parent.Length < Root.Length || !parent.StartsWith(Root, StringComparison.Ordinal) ? Root : parent);
	}

	private Task UseRootAsync()
	{
		_asRoot = true;
		return ReloadAsync();
	}

	/// <summary>A folder's entries or a file's text, whichever <paramref name="path"/> is.</summary>
	private async Task ShowAsync(string path)
	{
		if (HostFiles is not { } files)
		{
			_problem = "This session has no file system to read the docker host's files with.";
			return;
		}

		_busy = true;
		_problem = null;
		_needsRoot = false;
		try
		{
			RemoteFileEntry entry = await files.StatAsync(path, _asRoot, Lifetime)
				?? throw new RemoteFileSystemException(RemoteFileErrorKind.NotFound, $"{path} does not exist.", path);

			if (entry.IsDirectoryLike)
			{
				IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(path, _asRoot, Lifetime);
				_entries = [.. entries.OrderByDescending(item => item.IsDirectoryLike).ThenBy(item => item.Name, StringComparer.Ordinal)];
				_file = null;
			}
			else
			{
				_file = await files.ReadTextAsync(path, _asRoot, Lifetime);
			}

			_path = path;
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.PermissionDenied && !_asRoot)
		{
			_problem = $"The login may not read {path}.";
			_needsRoot = true;
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_problem = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_busy = false;
		}

		StateHasChanged();
	}
}
