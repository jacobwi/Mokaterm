using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Runs one command inside a container without a terminal: <c>docker exec</c> with the user, folder and environment
/// asked for, through the session's commands with a time limit and an output cap, and shows both streams and the exit.
/// The commands run here stay in a short list for the session only; nothing typed here is saved or logged. The
/// interactive shell is the other way in, typed into the session's terminal.
/// </summary>
public sealed partial class DockerExecPanel : DockerManagerPageBase
{
	private string _command = "";
	private string _user = "";
	private string _directory = "";
	private string _environment = "";
	private DockerExecShell _shell = DockerExecShell.Sh;
	private bool _options;
	private bool _running;
	private string? _problem;
	private DockerOutput? _result;

	[Parameter, EditorRequired]
	public string Container { get; set; } = "";

	[Parameter]
	public bool Running { get; set; }

	private IReadOnlyList<DockerExecRequest> History =>
		[.. (Feature?.ExecHistory ?? []).Where(entry => entry.Container == Container)];

	private string CommandPlaceholder => _shell == DockerExecShell.None ? "ls -la /etc" : "ps aux | head";

	private string WhoNote => _user.Trim().Length > 0
		? $"Runs as {_user.Trim()}."
		: "Runs as the container's own user, which is root unless the image names another.";

	private static string Duration(DockerOutput output) => output.Duration.TotalSeconds < 1
		? string.Create(CultureInfo.CurrentCulture, $"{output.Duration.TotalMilliseconds:0} ms")
		: string.Create(CultureInfo.CurrentCulture, $"{output.Duration.TotalSeconds:0.0} s");

	private static string HistoryLine(DockerExecRequest entry)
	{
		string options = (entry.User.Length > 0 ? $"-u {entry.User} " : "") + (entry.WorkingDirectory.Length > 0 ? $"-w {entry.WorkingDirectory} " : "");
		string shell = entry.Shell switch
		{
			DockerExecShell.Bash => "bash -c ",
			DockerExecShell.None => "",
			_ => "sh -c ",
		};

		return (options + shell + entry.Command).ReplaceLineEndings(" ");
	}

	private void OnCommandChanged(string? value) => _command = value ?? "";

	private void OnUserChanged(string? value) => _user = value ?? "";

	private void OnDirectoryChanged(string? value) => _directory = value ?? "";

	private void OnEnvironmentChanged(string? value) => _environment = value ?? "";

	private void OnShellChanged(string? value) =>
		_shell = Enum.TryParse(value, out DockerExecShell shell) ? shell : DockerExecShell.Sh;

	private void ToggleOptions() => _options = !_options;

	private Task OnCommandKeyDown(KeyboardEventArgs args) =>
		args.Key == "Enter" && (args.CtrlKey || args.MetaKey) ? RunAsync() : Task.CompletedTask;

	private void Load(DockerExecRequest entry)
	{
		_command = entry.Command;
		_shell = entry.Shell;
		_user = entry.User;
		_directory = entry.WorkingDirectory;
		_environment = string.Join('\n', entry.Environment);
		_options = entry.User.Length > 0 || entry.WorkingDirectory.Length > 0 || entry.Environment.Count > 0;
	}

	private async Task RunAsync()
	{
		if (_running || Client is not { } client || Feature is not { } feature)
		{
			return;
		}

		DockerExecRequest request = new()
		{
			Container = Container,
			Command = _command.Trim(),
			Shell = _shell,
			User = _user.Trim(),
			WorkingDirectory = _directory.Trim(),
			Environment = DockerExecRequest.ParseEnvironment(_environment),
		};

		if (request.Problem() is { } problem)
		{
			_problem = problem;
			return;
		}

		_problem = null;
		_running = true;
		StateHasChanged();
		try
		{
			_result = await client.ExecAsync(request, Lifetime);
			feature.RememberExec(request);
		}
		catch (Exception ex) when (Describe(ex) is { } failure)
		{
			_problem = failure;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_running = false;
		}

		StateHasChanged();
	}
}
