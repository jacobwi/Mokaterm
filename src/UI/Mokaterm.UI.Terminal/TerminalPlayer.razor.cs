using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Terminal.Interop;
using Mokaterm.UI.Terminal.Playback;

namespace Mokaterm.UI.Terminal;

/// <summary>
/// Replays a <see cref="TerminalRecording"/> in a read-only terminal, with play and pause, a seek bar, a restart and three
/// speeds. A recording without times (a plain or raw log) is shown whole at once, without the controls. Give it a
/// <c>@key</c> per recording: the terminal is built for the first one it is given.
/// </summary>
public sealed partial class TerminalPlayer : ComponentBase, IAsyncDisposable
{
	private static readonly int[] Speeds = [1, 2, 4];

	// The clock is checked this often while playing. The screen moves only when an event falls due, so a quiet stretch
	// costs a comparison per tick and no call into the page.
	private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(40);

	// The seek bar and the time are drawn every so many ticks rather than on every one, which on a Blazor Server
	// circuit is a render sent over the wire each time.
	private const int TicksPerRender = 6;

	// Events go into the page in batches of about this much text, so one recording is a few dozen messages at most.
	private const int LoadBatchCharacters = 256 * 1024;

	private const int LoadBatchEvents = 4096;

	private readonly CancellationTokenSource _disposeCts = new();
	private ElementReference _host;
	private ElementReference _screen;
	private RecordingClock? _clock;
	private CancellationTokenSource? _playing;
	private int _playerId;
	private int _shown = -1;
	private bool _ready;
	private bool _disposed;
	private string? _error;

	[Parameter, EditorRequired]
	public TerminalRecording Recording { get; set; } = default!;

	/// <summary>The terminal settings whose theme and font the playback uses.</summary>
	[Parameter, EditorRequired]
	public TerminalSettings Settings { get; set; } = new();

	[Inject]
	private TerminalInterop Interop { get; set; } = default!;

	[Inject]
	private ITerminalThemeCatalog ThemeCatalog { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<TerminalPlayer> Logger { get; set; } = default!;

	private RecordingClock Clock => _clock ??= new RecordingClock(Recording.Duration, Time);

	private bool IsPlaying => Clock.IsPlaying;

	private TimeSpan Position => Clock.Position;

	private double PositionSeconds => Math.Round(Position.TotalSeconds, 1);

	// A slider needs room to move even for a recording that all happens at once.
	private double DurationSeconds => Math.Max(0.1, Math.Round(Recording.Duration.TotalSeconds, 1, MidpointRounding.ToPositiveInfinity));

	private string PlayLabel => IsPlaying ? "Pause" : "Play";

	private string SpeedValue => Clock.Speed.ToString(CultureInfo.InvariantCulture);

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		StopLoop();
		await _disposeCts.CancelAsync();
		_disposeCts.Dispose();
		if (_playerId != 0)
		{
			try
			{
				await Interop.TryDisposePlayerAsync(_playerId);
			}
			catch (JSException ex)
			{
				Logger.LogDebug(ex, "Disposing the playback terminal failed.");
			}

			_playerId = 0;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender || _disposed)
		{
			return;
		}

		try
		{
			TerminalJsOptions options = TerminalJsOptions.From(Settings, ThemeCatalog.Get(Settings.ThemeId));
			int id = await Interop.CreatePlayerAsync(_host, _screen, options, Recording.Columns, Recording.Rows);
			if (_disposed)
			{
				await Interop.TryDisposePlayerAsync(id);
				return;
			}

			_playerId = id;
			await LoadAsync(id, _disposeCts.Token);
			_ready = true;
			if (Recording.IsTimed)
			{
				Clock.Play();
				StartLoop();
			}
			else
			{
				await ShowAsync(Recording.Events.Count);
			}
		}
		catch (JSException ex)
		{
			Logger.LogError(ex, "The playback terminal could not be created.");
			_error = "The recording could not be shown here.";
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The dialog closed while the recording was loading.
			return;
		}

		StateHasChanged();
	}

	private async Task LoadAsync(int id, CancellationToken cancellationToken)
	{
		List<TerminalJsPlayerEvent> batch = [];
		int characters = 0;
		foreach (RecordingEvent recorded in Recording.Events)
		{
			batch.Add(new TerminalJsPlayerEvent(recorded.Output, recorded.Columns, recorded.Rows));
			characters += recorded.Output?.Length ?? 0;
			if (characters >= LoadBatchCharacters || batch.Count >= LoadBatchEvents)
			{
				await Interop.LoadPlayerAsync(id, batch, cancellationToken);
				batch = [];
				characters = 0;
			}
		}

		if (batch.Count > 0)
		{
			await Interop.LoadPlayerAsync(id, batch, cancellationToken);
		}
	}

	private async Task TogglePlayAsync()
	{
		if (!_ready)
		{
			return;
		}

		if (Clock.IsPlaying)
		{
			Clock.Pause();
			StopLoop();
			await ShowPositionAsync();
			return;
		}

		Clock.Play();
		StartLoop();
	}

	private async Task RestartAsync()
	{
		if (!_ready)
		{
			return;
		}

		Clock.Seek(TimeSpan.Zero);
		await ShowPositionAsync();
		if (!Clock.IsPlaying)
		{
			Clock.Play();
			StartLoop();
		}
	}

	private async Task SeekAsync(double seconds)
	{
		if (!_ready || !double.IsFinite(seconds))
		{
			return;
		}

		Clock.Seek(TimeSpan.FromSeconds(Math.Max(0, seconds)));
		await ShowPositionAsync();
	}

	private void SetSpeed(string? value)
	{
		if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int speed) && Speeds.Contains(speed))
		{
			Clock.SetSpeed(speed);
		}
	}

	private void StartLoop()
	{
		StopLoop();
		CancellationTokenSource playing = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
		_playing = playing;
		_ = RunAsync(playing.Token);
	}

	private void StopLoop()
	{
		if (_playing is { } playing)
		{
			_playing = null;
			playing.Cancel();
			playing.Dispose();
		}
	}

	// Runs on the renderer's dispatcher, where it was started from, so the clock, the screen and the controls are only
	// ever touched from one place.
	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			using PeriodicTimer timer = new(TickInterval, Time);
			int ticks = 0;
			do
			{
				await ShowPositionAsync();
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				if (Clock.IsAtEnd)
				{
					Clock.Pause();
					StateHasChanged();
					return;
				}

				if (++ticks % TicksPerRender == 0)
				{
					StateHasChanged();
				}
			}
			while (await timer.WaitForNextTickAsync(cancellationToken));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Paused or closed.
		}
	}

	private Task ShowPositionAsync() => ShowAsync(Recording.IndexAt(Clock.Position));

	private async Task ShowAsync(int index)
	{
		if (_disposed || _playerId == 0 || index == _shown)
		{
			return;
		}

		_shown = index;
		try
		{
			await Interop.ShowPlayerAsync(_playerId, index);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Called from buttons and the seek bar, where an exception would end a Blazor Server circuit, and a frame the
			// page failed to draw is not worth that.
			Logger.LogDebug(ex, "Showing a point of the recording failed.");
		}
	}
}
