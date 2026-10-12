using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// What every page of the Docker manager shares, built in or from a plugin: a load with its spinner and its error,
/// changes that are confirmed with the exact commands and leave their outcome on screen (<see cref="ChangePanel"/>), and
/// polling that only runs while the page is visible. Register a page with <c>AddDockerPage</c>.
/// </summary>
public abstract partial class DockerPageBase : ComponentBase, IDisposable
{
	/// <summary>How many lines of a change's output the folded box keeps: the end, which is where the outcome is.</summary>
	private const int ChangeTailLines = 60;

	private readonly CancellationTokenSource _lifetime = new();
	private PagePoller? _poller;
	private CancellationTokenSource? _change;
	private ILogger? _logger;
	private bool _loading;
	private bool? _changeOutputShown;

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>False while the session's tab is hidden: polling waits instead of running commands nobody sees.</summary>
	[Parameter]
	public bool Visible { get; set; } = true;

	/// <summary>The manager around the page; null outside one, such as in a test.</summary>
	[CascadingParameter]
	protected IDockerPageContext? Context { get; set; }

	[Inject]
	protected IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	protected TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILoggerFactory LoggerFactory { get; set; } = default!;

	/// <summary>Cancelled when the page goes away.</summary>
	protected CancellationToken Lifetime => _lifetime.Token;

	/// <summary>The page went away; nothing it started should touch it any more.</summary>
	protected bool IsDisposed { get; private set; }

	/// <summary>True while the first load has not finished.</summary>
	protected bool IsFirstLoad { get; private set; } = true;

	protected bool IsLoading => _loading;

	/// <summary>Why the last load failed, or null.</summary>
	protected string? LoadError { get; set; }

	/// <summary>The change that is running, as its command, or null.</summary>
	protected string? RunningCommand { get; private set; }

	/// <summary>What the running change is, for example "Restart web".</summary>
	protected string? RunningTitle { get; private set; }

	protected bool IsBusy => RunningCommand is not null;

	/// <summary>The last change and how it ended, kept on screen until the next one or until dismissed.</summary>
	private DockerChange? LastChange { get; set; }

	/// <summary>A failed change opens its output; one that went through keeps it folded until asked.</summary>
	private bool ChangeOutputShown => _changeOutputShown ?? LastChange is { Succeeded: false };

	private ILogger Logger => _logger ??= LoggerFactory.CreateLogger(GetType());

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>Stops everything the page started. The lifetime token is cancelled but kept, since a load may still be finishing with it.</summary>
	protected virtual void Dispose(bool disposing)
	{
		if (IsDisposed || !disposing)
		{
			return;
		}

		IsDisposed = true;
		_lifetime.Cancel();
		_poller?.Dispose();
		_change?.Cancel();
	}

	/// <summary>
	/// Runs a load: one at a time, its failure kept in <see cref="LoadError"/> rather than thrown, and nothing at all once
	/// the page is gone.
	/// </summary>
	protected async Task LoadAsync(Func<CancellationToken, Task> load)
	{
		ArgumentNullException.ThrowIfNull(load);
		if (_loading || IsDisposed)
		{
			return;
		}

		_loading = true;
		try
		{
			await load(Lifetime);
			LoadError = null;
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			LoadError = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_loading = false;
			IsFirstLoad = false;
		}

		if (!IsDisposed)
		{
			StateHasChanged();
		}
	}

	/// <summary>Runs a read through the manager and hands back its stdout; see <see cref="IDockerPageContext.ReadAsync"/>.</summary>
	/// <exception cref="DockerOperationException">The read failed, or the page is not inside a manager.</exception>
	protected Task<string> ReadAsync(DockerCommand command, CancellationToken cancellationToken) =>
		Context is { } context
			? context.ReadAsync(command, cancellationToken)
			: throw new DockerOperationException("The Docker manager is not open.");

	/// <summary><see cref="RunChangeAsync(IReadOnlyList{DockerCommand}, DockerChangePrompt, Func{Task})"/> for one command; the output, or null when it did not run.</summary>
	protected async Task<DockerOutput?> RunChangeAsync(DockerCommand command, DockerChangePrompt prompt, Func<Task>? afterwards = null)
	{
		ArgumentNullException.ThrowIfNull(command);
		IReadOnlyList<DockerOutput>? outputs = await RunChangeAsync([command], prompt, afterwards);
		return outputs is [.., var last] ? last : null;
	}

	/// <summary>
	/// Runs several commands as one change, one after the other, stopping at the first that fails: "pull first, then up".
	/// The question, when the prompt has one, shows every command; whatever happens, the outcome stays in
	/// <see cref="ChangePanel"/>, and <paramref name="afterwards"/> (a refresh) runs once it ended. Each command runs as a
	/// change. Null when the user said no, the page is busy, or it is not inside a manager.
	/// </summary>
	protected async Task<IReadOnlyList<DockerOutput>?> RunChangeAsync(IReadOnlyList<DockerCommand> commands, DockerChangePrompt prompt, Func<Task>? afterwards = null)
	{
		ArgumentNullException.ThrowIfNull(commands);
		if (Context is not { } context || commands.Count == 0)
		{
			return null;
		}

		DockerCommand[] changes = [.. commands.Select(command => command.IsChange ? command : command.AsChange())];
		return await RunChangeStepsAsync(
			[.. changes.Select(context.Display)],
			prompt,
			(index, cancellationToken) => context.RunAsync(changes[index], cancellationToken),
			() => context.BeginChange(changes),
			afterwards);
	}

	/// <summary>
	/// The change machinery under <see cref="RunChangeAsync(IReadOnlyList{DockerCommand}, DockerChangePrompt, Func{Task})"/>,
	/// for a page that runs its steps some other way: <paramref name="shown"/> is each step as the question and the outcome
	/// show it, <paramref name="step"/> runs the step at an index, and <paramref name="note"/> marks the change for the
	/// health watch while it runs.
	/// </summary>
	protected async Task<IReadOnlyList<DockerOutput>?> RunChangeStepsAsync(
		IReadOnlyList<string> shown,
		DockerChangePrompt prompt,
		Func<int, CancellationToken, Task<DockerOutput>> step,
		Func<IDisposable?>? note = null,
		Func<Task>? afterwards = null)
	{
		ArgumentNullException.ThrowIfNull(shown);
		ArgumentNullException.ThrowIfNull(prompt);
		ArgumentNullException.ThrowIfNull(step);
		if (IsBusy || IsDisposed || shown.Count == 0)
		{
			return null;
		}

		string title = prompt.Title;
		if (prompt.Confirm is { } confirm)
		{
			ConfirmResult answer = await Interaction.ConfirmAsync(
				new ConfirmPrompt
				{
					Title = title,
					Message = confirm,
					ConfirmText = prompt.ConfirmText,
					Destructive = prompt.Destructive,
					Details = string.Join('\n', shown),
				},
				Lifetime);

			if (!answer.Confirmed || IsDisposed)
			{
				return null;
			}
		}

		using IDisposable? noted = note?.Invoke();
		using CancellationTokenSource change = CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
		_change = change;
		RunningTitle = title;
		LastChange = null;
		_changeOutputShown = null;
		List<DockerOutput> outputs = [];
		try
		{
			for (int index = 0; index < shown.Count; index++)
			{
				RunningCommand = shown[index];
				StateHasChanged();
				DockerOutput output = await step(index, change.Token);
				outputs.Add(output);
				LastChange = new DockerChange(title, string.Join(" && ", shown.Take(index + 1)), Combine(outputs), null);
				if (!output.Succeeded)
				{
					break;
				}
			}
		}
		catch (DockerOperationException ex)
		{
			LastChange = new DockerChange(title, string.Join(" && ", shown.Take(outputs.Count + 1)), ex.Output ?? (outputs.Count > 0 ? Combine(outputs) : null), ex.Message);
		}
		catch (OperationCanceledException) when (!Lifetime.IsCancellationRequested)
		{
			LastChange = new DockerChange(title, string.Join(" && ", shown.Take(outputs.Count + 1)), null, "Stopped waiting. The daemon may still finish what it started.");
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		finally
		{
			_change = null;
			RunningCommand = null;
			RunningTitle = null;
		}

		StateHasChanged();
		if (afterwards is not null && !IsDisposed)
		{
			await afterwards();
		}

		return outputs;
	}

	/// <summary>Copies plain text, saying so; for names and ids, never for anything secret.</summary>
	protected async Task CopyTextAsync(IClipboardService clipboard, string text, string what)
	{
		ArgumentNullException.ThrowIfNull(clipboard);
		try
		{
			await clipboard.WriteTextAsync(text, Lifetime);
			Interaction.Notify(NoticeSeverity.Success, $"Copied the {what}.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Interaction.Notify(NoticeSeverity.Error, "Could not copy to the clipboard.");
		}
	}

	/// <summary>Stops waiting for the running change and closes its channel.</summary>
	protected void CancelChange() => _change?.Cancel();

	protected void DismissChange() => LastChange = null;

	protected void ToggleChangeOutput() => _changeOutputShown = !ChangeOutputShown;

	/// <summary>
	/// Calls <paramref name="tick"/> every <paramref name="interval"/> while the page is visible and nothing else is
	/// loading or running. Replaces any polling already running.
	/// </summary>
	protected void StartPolling(TimeSpan interval, Func<Task> tick)
	{
		if (IsDisposed)
		{
			return;
		}

		_poller ??= new PagePoller(Time, InvokeAsync, () => Visible && !_loading && !IsBusy);
		_poller.Start(interval, tick, Lifetime);
	}

	protected void StopPolling() => _poller?.Stop();

	/// <summary>The sentence for a failure a page shows, or null for one that is not the user's business (a cancel).</summary>
	protected string? Describe(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		switch (exception)
		{
			case DockerOperationException docker:
				return docker.Message;
			case RemoteFileSystemException files:
				return files.Message;
			case ArgumentException argument:
				return argument.Message;
			case OperationCanceledException:
				return null;
			default:
				Logger.LogWarning("A Docker manager page failed: {Reason}", LogSafe.Describe(exception));
				return exception.Message;
		}
	}

	/// <summary>The last lines of a change's output, which say how it ended.</summary>
	private static string ChangeTail(DockerChange change)
	{
		string[] lines = change.Text.Split('\n');
		return lines.Length <= ChangeTailLines ? change.Text : string.Join('\n', lines[^ChangeTailLines..]);
	}

	/// <summary>Several outputs as one, for a change of several commands: the exit is the last one's.</summary>
	private static DockerOutput Combine(List<DockerOutput> outputs) => outputs.Count == 1
		? outputs[0]
		: outputs[^1] with
		{
			Command = string.Join(" && ", outputs.Select(output => output.Command)),
			Output = string.Join('\n', outputs.Select(output => output.Output.TrimEnd()).Where(text => text.Length > 0)),
			Error = string.Join('\n', outputs.Select(output => output.Error.TrimEnd()).Where(text => text.Length > 0)),
		};
}
