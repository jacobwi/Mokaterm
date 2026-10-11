using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// The Linux system view of a session: what the server is in a strip at the top, a page per area, a review panel
/// that shows the exact commands of a change before it runs, and the last change's result at the bottom.
/// </summary>
/// <remarks>
/// It opens as a session tool, which is the only surface the shell gives a module beside a terminal. The navigation
/// shows labels when the view is wide, icons when it is not, and a dropdown when it is narrow; each page lays out its
/// list and detail panel side by side or one above the other by its own width.
/// </remarks>
public sealed partial class SystemAdminView : SessionViewBase, IAsyncDisposable
{
	private static readonly IReadOnlyList<AdminPage> AllPages = Enum.GetValues<AdminPage>();

	private readonly CancellationTokenSource _closed = new();
	private ISessionHandle? _handle;
	private IProtocolSession? _attached;
	private SystemAdminFeature? _feature;
	private SystemAdminFeature? _ownedFeature;
	private SystemProfile? _profile;
	private AdminPageContext? _context;
	private AdminPage _page = AdminPage.Overview;
	private bool _detecting;
	private string? _detectError;
	private bool _rootBusy;
	private string? _rootError;

	private AdminActionResult? _shownResult;
	private bool _outputOpen;

	private AdminAction? _review;
	private AdminInput? _reviewInput;
	private TaskCompletionSource<bool>? _reviewAnswer;
	private ElementReference _reviewDefault;
	private bool _focusReview;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<SystemAdminView> Logger { get; set; } = default!;

	private bool IsBusy => _review is not null || _feature?.Running is not null;

	private AdminActionResult? Result => _feature?.Running ?? _feature?.Last;

	public async ValueTask DisposeAsync()
	{
		if (_handle is { } handle)
		{
			handle.Changed -= OnSessionChanged;
			_handle = null;
		}

		_reviewAnswer?.TrySetResult(false);
		await DetachAsync();
		await _closed.CancelAsync();
		_closed.Dispose();
	}

	protected override async Task OnParametersSetAsync()
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

		await AttachAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_focusReview)
		{
			_focusReview = false;
			try
			{
				await _reviewDefault.FocusAsync();
			}
			catch (Exception ex) when (ex is InvalidOperationException or Microsoft.JSInterop.JSException or TaskCanceledException)
			{
				// The panel closed before the browser got there.
			}
		}
	}

	/// <summary>Init system, package manager, firewall and container, in the strip's order.</summary>
	private static IEnumerable<string> SystemParts(SystemProfile profile)
	{
		yield return profile.Init switch
		{
			InitSystem.Systemd => "systemd",
			InitSystem.OpenRc => "OpenRC",
			InitSystem.SysV => "sysvinit",
			InitSystem.BusyBox => "BusyBox init",
			_ => "no init",
		};

		if (profile.Capabilities.Packages is { } packages)
		{
			yield return packages.DisplayName;
		}

		if (profile.Capabilities.Firewall is { } firewall)
		{
			yield return firewall.DisplayName;
		}

		if (profile.IsContainer)
		{
			yield return "container";
		}
	}

	/// <summary>Every page, with the reason the detected system cannot use it. Such pages stay in the list, greyed.</summary>
	private IEnumerable<AdminNavEntry> Entries(SystemProfile profile)
	{
		SystemCapabilities capabilities = profile.Capabilities;
		yield return new(AdminPage.Overview, "Overview", SshIcons.Pulse, null);
		yield return new(AdminPage.Services, "Services", SshIcons.Gear, capabilities.Services is null ? capabilities.ServicesMissing : null);
		yield return new(AdminPage.Packages, "Packages", SshIcons.Package, capabilities.Packages is null ? capabilities.PackagesMissing : null);
		yield return new(AdminPage.Cron, "Cron", SshIcons.Calendar, capabilities.HasCrontab || capabilities.HasTimers ? null : "Neither cron nor systemd timers are installed.");
		yield return new(AdminPage.Time, "Time", SshIcons.Clock, null);
		yield return new(AdminPage.HostName, "Hostname", MokatermIcons.Tag, null);
		yield return new(AdminPage.Network, "Network", SshIcons.Network, capabilities.HasNetworkTools ? null : "There is no ip command (iproute2), so only name servers and ports can be read.");
		yield return new(AdminPage.Users, "Users", SshIcons.Users, null);
		yield return new(AdminPage.Disks, "Disks", SshIcons.Disk, null);
		yield return new(AdminPage.Processes, "Processes", SshIcons.Chip, capabilities.HasProcessTools ? null : "There is no ps command.");
		yield return new(AdminPage.Logs, "Logs", SshIcons.Log, null);
		yield return new(AdminPage.Firewall, "Firewall", MokatermIcons.Shield, capabilities.Firewall is null
			? capabilities.FirewallMissing
			: _feature?.RootBlockedReason is { } blocked ? $"The firewall only answers root. {blocked}" : null);
	}

	private string NavClass(AdminNavEntry entry) =>
		"mta-nav-item" + (entry.Page == _page ? " mta-nav-item--active" : "") + (entry.Unavailable is not null ? " mta-nav-item--off" : "");

	private string SelectLabel(SystemProfile profile, AdminPage page)
	{
		AdminNavEntry entry = Entries(profile).First(candidate => candidate.Page == page);
		return entry.Unavailable is null ? entry.Label : $"{entry.Label} (not on this system)";
	}

	private static MokaColor ResultColor(AdminActionResult result) =>
		result.IsRunning ? MokaColor.Warning : result.Succeeded ? MokaColor.Success : MokaColor.Error;

	private static string ResultClass(AdminActionResult result) =>
		result.IsRunning ? "mta-result mta-result--running" : result.Succeeded ? "mta-result" : "mta-result mta-result--failed";

	private static string ResultLabel(AdminActionResult result) => result switch
	{
		{ IsRunning: true } => "RUNNING",
		{ Failure: not null } => "DID NOT FINISH",
		{ Succeeded: true } => "OK",
		_ => "FAILED",
	};

	/// <summary>Exit code and duration in one mono run, such as <c>exit 0 · 1.2 s</c>.</summary>
	private static string ResultMeta(AdminActionResult result)
	{
		if (result.Outcome is not { } outcome)
		{
			return "";
		}

		string exit = outcome.ExitCode is { } code
			? string.Create(CultureInfo.InvariantCulture, $"exit {code}")
			: $"signal {outcome.ExitSignal ?? "?"}";
		string duration = outcome.Duration.TotalSeconds < 1
			? string.Create(CultureInfo.InvariantCulture, $"{outcome.Duration.TotalMilliseconds:0} ms")
			: outcome.Duration.TotalMinutes < 1
				? string.Create(CultureInfo.InvariantCulture, $"{outcome.Duration.TotalSeconds:0.0} s")
				: DisplayFormat.Duration(outcome.Duration);
		return $"{exit} · {duration}";
	}

	private static string ResultText(AdminActionResult result)
	{
		if (result.Failure is { } failure)
		{
			return failure;
		}

		if (result.Outcome is not { } outcome)
		{
			return "";
		}

		string output = AdminText.Clean(outcome.Output).TrimEnd();
		string error = AdminText.Clean(outcome.Error).TrimEnd();
		string text = (output, error) switch
		{
			({ Length: > 0 }, { Length: > 0 }) => output + "\n\n" + error,
			({ Length: > 0 }, _) => output,
			(_, { Length: > 0 }) => error,
			_ => "(no output)",
		};

		return outcome.IsTruncated ? text + "\n\n(the output was longer; only its start is kept)" : text;
	}

	/// <summary>A new result opens its output when it failed and keeps it folded when it went through.</summary>
	private bool OutputOpen(AdminActionResult result)
	{
		if (!ReferenceEquals(result, _shownResult))
		{
			_shownResult = result;
			_outputOpen = !result.IsRunning && !result.Succeeded;
		}

		return _outputOpen;
	}

	private string RunsAs(AdminAction action)
	{
		if (!action.Script.AsRoot)
		{
			return $"Runs as {_profile?.Facts.UserName}, without sudo";
		}

		if (_feature?.LoginIsRoot == true)
		{
			return "Runs as root, the login itself";
		}

		return _feature?.RootModeOn == true ? "Runs as root through sudo" : "Runs as root through sudo, which may ask for the password";
	}

	private async Task AttachAsync()
	{
		IProtocolSession? session = Session.Session;
		if (ReferenceEquals(session, _attached))
		{
			return;
		}

		await DetachAsync();
		_attached = session;
		if (session is null)
		{
			return;
		}

		_feature = session.GetFeature<SystemAdminFeature>();
		if (_feature is null && session.GetFeature<IRemoteCommandFeature>() is { } commands)
		{
			// A session that can run commands but is not SSH (the DevHost's demo): reads only, owned by this view.
			_ownedFeature = new SystemAdminFeature(
				new CommandFeatureAdminShell(commands, Session.Connection.GetTitle(Session.Host)),
				Time,
				Logger);
			_feature = _ownedFeature;
		}

		if (_feature is { } feature)
		{
			feature.Changed += OnFeatureChanged;
			_profile = feature.Profile;
			BuildContext();
			await DetectAsync(refresh: false);
		}
	}

	private async Task DetachAsync()
	{
		if (_feature is { } feature)
		{
			feature.Changed -= OnFeatureChanged;

			// Root mode lasts as long as the view the user can see; a running change keeps sudo until it finishes.
			if (!ReferenceEquals(feature, _ownedFeature))
			{
				feature.DisableRoot();
			}
		}

		if (_ownedFeature is { } owned)
		{
			_ownedFeature = null;
			await owned.DisposeAsync();
		}

		_feature = null;
		_attached = null;
		_profile = null;
		_context = null;
	}

	private async Task DetectAsync(bool refresh)
	{
		if (_feature is not { } feature)
		{
			return;
		}

		_detecting = true;
		_detectError = null;
		StateHasChanged();
		try
		{
			_profile = await feature.DetectAsync(refresh, _closed.Token);
			BuildContext();
		}
		catch (AdminException ex)
		{
			_detectError = ex.Message;
		}
		catch (OperationCanceledException) when (_closed.IsCancellationRequested)
		{
			return;
		}
		finally
		{
			_detecting = false;
		}
	}

	private void BuildContext()
	{
		if (_feature is not { } feature || _profile is not { } profile)
		{
			_context = null;
			return;
		}

		if (_context is not null && ReferenceEquals(_context.Profile, profile) && ReferenceEquals(_context.Feature, feature))
		{
			return;
		}

		_context = new AdminPageContext
		{
			Feature = feature,
			Profile = profile,
			Run = ReviewAndRunAsync,
			EnableRoot = EnableRootAsync,
			Navigate = Show,
			SessionAddress = Session.Host.Address,
			IsBusy = () => IsBusy,
		};
	}

	private void Show(AdminPage page)
	{
		_page = page;
		_ = InvokeAsync(StateHasChanged);
	}

	private Task RefreshDetectionAsync() => DetectAsync(refresh: true);

	private async Task EnableRootAsync()
	{
		if (_feature is not { } feature || _rootBusy)
		{
			return;
		}

		_rootBusy = true;
		_rootError = null;
		try
		{
			await feature.EnableRootAsync(_closed.Token);
		}
		catch (AdminException ex)
		{
			_rootError = ex.Message;
		}
		catch (OperationCanceledException) when (_closed.IsCancellationRequested)
		{
		}
		finally
		{
			_rootBusy = false;
		}
	}

	private void DisableRoot()
	{
		_rootError = null;
		_feature?.DisableRoot();
	}

	/// <summary>Shows the review panel and waits for Run or Cancel; on Run, runs the action for the session.</summary>
	private async Task<AdminActionResult?> ReviewAndRunAsync(AdminAction action, AdminInput? input)
	{
		if (_feature is not { } feature || _review is not null)
		{
			return null;
		}

		TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_review = action;
		_reviewInput = input;
		_reviewAnswer = answer;
		_focusReview = true;
		StateHasChanged();

		bool confirmed;
		try
		{
			confirmed = await answer.Task;
		}
		finally
		{
			_review = null;
			_reviewInput = null;
			_reviewAnswer = null;
		}

		if (!confirmed)
		{
			StateHasChanged();
			return null;
		}

		try
		{
			return await feature.RunAsync(action, input);
		}
		catch (AdminException ex)
		{
			_rootError = ex.Message;
			return null;
		}
	}

	private void AnswerReview(bool run) => _reviewAnswer?.TrySetResult(run);

	private void OnReviewKeyDown(KeyboardEventArgs args)
	{
		if (args.Key == "Escape")
		{
			AnswerReview(false);
		}
	}

	private void ToggleOutput() => _outputOpen = !_outputOpen;

	private void OnFeatureChanged() => _ = InvokeAsync(() =>
	{
		if (_feature is { } feature && feature.Profile is { } profile && !ReferenceEquals(profile, _profile) && !_detecting)
		{
			_profile = profile;
			BuildContext();
		}

		StateHasChanged();
	});

	private void OnSessionChanged() => _ = InvokeAsync(async () =>
	{
		await AttachAsync();
		StateHasChanged();
	});
}
