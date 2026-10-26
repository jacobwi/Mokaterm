using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The Docker manager of an SSH session: looks for Docker on the server once per session, explains what is missing when
/// it cannot be used (offering sudo when the socket refuses the account), and otherwise shows its pages beside a
/// navigation that folds to icons when the dialog is narrow. The pages are the registered
/// <see cref="DockerPageDescriptor"/>s, this module's and any other's, opened by id. While it is open it also watches the
/// host's containers and says when one turns unhealthy, stops, is restarted on its own or keeps restarting.
/// </summary>
public sealed partial class DockerManagerView : SessionViewBase, IDisposable
{
	/// <summary>How often the health watch reads every container's state while the manager shows.</summary>
	private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(10);

	private readonly CancellationTokenSource _lifetime = new();
	private readonly DockerPageContext _context;
	private PagePoller? _watchPoller;
	private DockerHealthWatch _watch = new();
	private bool _watching;
	private ISessionHandle? _handle;
	private IProtocolSession? _attached;
	private DockerSessionFeature? _feature;
	private DockerEngineStatus? _status;
	private string _page = DockerPageIds.Containers;
	private bool _detecting;
	private bool _elevating;
	private string? _elevationError;

	public DockerManagerView() => _context = new DockerPageContext(
		page =>
		{
			_page = page;
			StateHasChanged();
		},
		() => _feature);

	[Inject]
	private ILogger<DockerManagerView> Logger { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	/// <summary>The page on show: the one picked, or the first when no page has that id (one a plugin took with it).</summary>
	private DockerPageDescriptor? CurrentPage =>
		Contributions.DockerPages.FirstOrDefault(page => page.Id == _page) ?? (Contributions.DockerPages is [var first, ..] ? first : null);

	/// <summary>What every page takes, built in or not: the session and whether its tab is the one on show.</summary>
	private Dictionary<string, object?> PageParameters => new(StringComparer.Ordinal)
	{
		[nameof(DockerPageBase.Session)] = Session,
		[nameof(DockerPageBase.Visible)] = IsActive,
	};

	private MokaColor HeadColor => _status?.Availability switch
	{
		DockerAvailability.Ready => MokaColor.Success,
		DockerAvailability.PermissionDenied or DockerAvailability.DaemonUnreachable => MokaColor.Warning,
		_ => MokaColor.Error,
	};

	private string HeadLabel => _status switch
	{
		{ Availability: DockerAvailability.Ready, Version.ServerVersion: { Length: > 0 } server } => $"DOCKER {server}",
		{ Availability: DockerAvailability.Ready } => "DOCKER",
		{ Availability: DockerAvailability.PermissionDenied } => "DOCKER REFUSED",
		{ Availability: DockerAvailability.DaemonUnreachable } => "DAEMON DOWN",
		{ Availability: DockerAvailability.NotInstalled } => "NO DOCKER",
		_ => "DOCKER FAILED",
	};

	private string UnavailableTitle => _status?.Availability switch
	{
		DockerAvailability.NotInstalled => "Docker is not installed",
		DockerAvailability.DaemonUnreachable => "The Docker daemon is not answering",
		_ => "Docker could not be checked",
	};

	private string DockerGroupCommand => $"sudo usermod -aG docker {_feature?.UserName}";

	public void Dispose()
	{
		_lifetime.Cancel();
		_watchPoller?.Dispose();
		Detach();
		if (_handle is { } handle)
		{
			handle.Changed -= OnSessionChanged;
			_handle = null;
		}
	}

	protected override void OnParametersSet()
	{
		if (!ReferenceEquals(_handle, Session))
		{
			if (_handle is { } previous)
			{
				previous.Changed -= OnSessionChanged;
			}

			_handle = Session;
			_handle.Changed += OnSessionChanged;
		}

		Attach();
	}

	/// <summary>Every page, with the reason this docker host cannot use it. Such pages stay in the list, greyed.</summary>
	private List<ToolNavEntry> NavEntries() =>
	[
		.. Contributions.DockerPages.Select(page => new ToolNavEntry(
			page.Id,
			page.Title,
			page.Icon,
			ToolPageRules.Evaluate(page.Unavailable, (IDockerPageContext)_context, Logger, page.Id))),
	];

	private static string ComposeLabel(ComposeTool compose) =>
		compose.Program == DockerProgram.ComposePlugin ? $"COMPOSE {compose.Version}" : $"DOCKER-COMPOSE {compose.Version}";

	private void Show(string page) => _page = page;

	private void Attach()
	{
		IProtocolSession? session = Session.Session;
		if (ReferenceEquals(session, _attached))
		{
			return;
		}

		Detach();
		_attached = session;
		_feature = session?.GetFeature<DockerSessionFeature>();
		_status = _feature?.Status;
		_elevationError = null;
		_watch = new DockerHealthWatch(_feature?.RestartLoops);
		if (_feature is { } feature)
		{
			feature.Changed += OnFeatureChanged;
			if (_status is null)
			{
				_ = DetectAsync();
			}
			else
			{
				StartWatching();
			}
		}
	}

	/// <summary>
	/// Reads every container once for the baseline, then again on each interval while the manager shows. A read that
	/// fails is skipped: the pages say what is wrong with the daemon, and the next read starts over from there.
	/// </summary>
	private void StartWatching()
	{
		if (_status is not { IsReady: true } || _lifetime.IsCancellationRequested || _watchPoller is { IsRunning: true })
		{
			return;
		}

		_watchPoller ??= new PagePoller(Time, InvokeAsync, () => IsActive);
		_watchPoller.Start(WatchInterval, WatchAsync, _lifetime.Token);
		_ = InvokeAsync(WatchAsync);
	}

	private async Task WatchAsync()
	{
		if (_feature is not { Status.IsReady: true } feature || _watching)
		{
			return;
		}

		_watching = true;
		DateTimeOffset started = Time.GetUtcNow();
		IReadOnlyList<DockerContainerState> states;
		try
		{
			states = await feature.Client.ContainerStatesAsync(_lifetime.Token);
		}
		catch (Exception ex) when (ex is DockerCommandException or OperationCanceledException or ObjectDisposedException)
		{
			Logger.LogDebug("The health watch skipped a read: {Reason}", LogSafe.Describe(ex));
			return;
		}
		finally
		{
			_watching = false;
		}

		if (!ReferenceEquals(feature, _feature))
		{
			return;
		}

		foreach (DockerHealthAlert alert in _watch.Observe(states, started, Time.GetUtcNow(), feature.Changes))
		{
			Interaction.Notify(alert.IsError ? NoticeSeverity.Error : NoticeSeverity.Warning, alert.Message, $"Docker on {feature.Host}");
		}
	}

	private void Detach()
	{
		if (_feature is { } feature)
		{
			feature.Changed -= OnFeatureChanged;
		}

		_feature = null;
		_attached = null;
		_watchPoller?.Stop();
	}

	// The feature raises this from whichever thread finished looking.
	private void OnFeatureChanged() => _ = InvokeAsync(() =>
	{
		_status = _feature?.Status;
		StartWatching();
		StateHasChanged();
	});

	private void OnSessionChanged() => _ = InvokeAsync(() =>
	{
		Attach();
		StateHasChanged();
	});

	private async Task DetectAsync()
	{
		if (_feature is not { } feature || _detecting)
		{
			return;
		}

		_detecting = true;
		StateHasChanged();
		try
		{
			_status = await feature.DetectAsync(_lifetime.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (ObjectDisposedException)
		{
			// The session closed while Docker was being looked for.
			return;
		}
		finally
		{
			_detecting = false;
		}

		StateHasChanged();
	}

	private async Task ElevateAsync()
	{
		if (_feature is not { } feature || _elevating)
		{
			return;
		}

		_elevating = true;
		_elevationError = null;
		try
		{
			_status = await feature.ElevateAsync(_lifetime.Token);
		}
		catch (ObjectDisposedException ex)
		{
			Logger.LogDebug("The session closed while sudo was starting: {Reason}", LogSafe.Describe(ex));
			return;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception ex) when (ex is RemoteFileSystemException or DockerCommandException or InvalidOperationException)
		{
			_elevationError = ex.Message;
		}
		finally
		{
			_elevating = false;
		}

		StateHasChanged();
	}
}
