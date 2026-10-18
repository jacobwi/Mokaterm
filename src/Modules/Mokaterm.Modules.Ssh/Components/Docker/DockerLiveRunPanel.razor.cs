using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Enums;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Draws the session's build or push that belongs to <see cref="Owner"/>: its output in the shared <see cref="LogView"/>,
/// copied from the run a few times a second while it runs and the page is visible, never per line. The run's own lines
/// are bounded, and so is what is drawn.
/// </summary>
public sealed partial class DockerLiveRunPanel : DockerManagerPageBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

	private readonly LogTail _tail = new();
	private DockerLiveRuns? _watched;
	private DockerLiveRun? _drawn;
	private int _drawnVersion = -1;

	/// <summary>Which runs this panel shows: <c>images</c>, or <c>compose:PROJECT</c>.</summary>
	[Parameter, EditorRequired]
	public string Owner { get; set; } = "";

	/// <summary>Leaves the panel; a run that is still going carries on.</summary>
	[Parameter]
	public EventCallback OnBack { get; set; }

	[Parameter]
	public string BackLabel { get; set; } = "Back";

	private DockerLiveRun? Run => LiveRuns?.Current is { } run && string.Equals(run.Owner, Owner, StringComparison.Ordinal) ? run : null;

	internal static string Elapsed(TimeSpan time) => time.TotalHours >= 1
		? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
		: time.ToString(@"m\:ss", CultureInfo.InvariantCulture);

	protected override void OnInitialized()
	{
		Watch();
		_ = Sync();
		StartPolling(RefreshInterval, TickAsync);
	}

	protected override void OnParametersSet()
	{
		Watch();
		_ = Sync();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _watched is { } watched)
		{
			watched.Changed -= OnRunsChanged;
			_watched = null;
		}

		base.Dispose(disposing);
	}

	private static MokaColor StateColor(DockerLiveState state) => state switch
	{
		DockerLiveState.Running => MokaColor.Warning,
		DockerLiveState.Succeeded => MokaColor.Success,
		DockerLiveState.Stopped => MokaColor.Surface,
		_ => MokaColor.Error,
	};

	private static string DroppedText(long dropped) => string.Create(
		CultureInfo.CurrentCulture,
		$"The first {dropped:N0} {(dropped == 1 ? "line is" : "lines are")} no longer kept: a run keeps its last {LogTail.MaxLines:N0}.");

	private string Elapsed(DockerLiveRun run) =>
		run.Duration is { } duration ? Elapsed(duration) : Elapsed(Time.GetUtcNow() - run.StartedAt);

	/// <summary>Follows the session's runs; the feature changes when the session reconnects.</summary>
	private void Watch()
	{
		DockerLiveRuns? runs = LiveRuns;
		if (ReferenceEquals(runs, _watched))
		{
			return;
		}

		if (_watched is { } old)
		{
			old.Changed -= OnRunsChanged;
		}

		_watched = runs;
		if (runs is not null)
		{
			runs.Changed += OnRunsChanged;
		}
	}

	private void OnRunsChanged() => _ = InvokeAsync(() =>
	{
		if (!IsDisposed)
		{
			_ = Sync();
			StateHasChanged();
		}
	});

	private Task TickAsync()
	{
		if (Sync())
		{
			StateHasChanged();
		}

		return Task.CompletedTask;
	}

	/// <summary>Copies the run's lines into the drawn tail when they changed. True when the panel should draw again.</summary>
	private bool Sync()
	{
		DockerLiveRun? run = Run;
		if (run is null)
		{
			if (_drawn is null)
			{
				return false;
			}

			_drawn = null;
			_drawnVersion = -1;
			_tail.Reset([]);
			return true;
		}

		int version = run.Lines.Version;
		if (ReferenceEquals(run, _drawn) && version == _drawnVersion)
		{
			// The time beside the title moves while it runs.
			return run.IsRunning;
		}

		_drawn = run;
		_drawnVersion = version;
		_tail.Reset(run.Lines.Snapshot());
		return true;
	}

	private async Task DismissAsync()
	{
		if (Run is { } run)
		{
			LiveRuns?.Dismiss(run);
		}

		if (OnBack.HasDelegate)
		{
			await OnBack.InvokeAsync();
		}
	}
}
