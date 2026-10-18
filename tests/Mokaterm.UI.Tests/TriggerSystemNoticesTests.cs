using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Terminal.Triggers;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The system notification beside a trigger's toast: only while nobody looks at the window, at most once per rule and
/// session per interval, naming the session and the rule and nothing more unless asked, and coming back with ids alone.
/// </summary>
public sealed class TriggerSystemNoticesTests
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(TerminalTriggerSettings.NotifyIntervalSeconds);

	private static readonly TerminalTrigger Errors = new() { Id = Guid.NewGuid(), Pattern = "ERROR|FATAL", Notify = true };

	private readonly FakeTimeProvider _time = new();
	private readonly FakeSettingsService _settings = new();
	private readonly FakeNotifications _system = new();
	private readonly TriggerSource _source = new(Guid.NewGuid(), Guid.NewGuid(), "bc@build-01.example.com");

	[Fact]
	public void InTheBackground_AMatch_IsShown()
	{
		Assert.True(Notices().Raise(_source, Errors, "ERROR: disk full"));

		SystemNotification shown = Assert.Single(_system.Shown);
		Assert.Equal("Trigger in bc@build-01.example.com", shown.Title);
		Assert.Equal("Matched \"ERROR|FATAL\"", shown.Message);
		Assert.Equal(_source.SessionId, shown.SessionId);
		Assert.Equal(_source.ConnectionId, shown.ConnectionId);
	}

	// The system keeps its notifications after the app has gone, and a server's line can hold a token.
	[Fact]
	public void ByDefault_TheMatchedLine_StaysOutOfTheNotification()
	{
		_ = Notices().Raise(_source, Errors, "ERROR: login failed for token=hunter2");

		SystemNotification shown = Assert.Single(_system.Shown);
		Assert.Null(shown.Detail);
		Assert.DoesNotContain("hunter2", shown.Title + shown.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void WithTheLineAskedFor_ItIsTheThirdLine_OnALineOfItsOwn()
	{
		_settings.Set(new TerminalTriggerSettings { SystemNotificationShowsLine = true });

		_ = Notices().Raise(_source, Errors, "\u0007ERROR:\tdisk\r\nfull");

		Assert.Equal("ERROR: disk full", Assert.Single(_system.Shown).Detail);
	}

	[Fact]
	public void InTheForeground_TheToastIsEnough_AndTheLimitIsNotUsedUp()
	{
		TriggerSystemNotices notices = Notices();
		_system.Foreground = true;

		Assert.False(notices.Raise(_source, Errors, "ERROR"));
		Assert.Empty(_system.Shown);

		_system.Foreground = false;
		Assert.True(notices.Raise(_source, Errors, "ERROR"));
	}

	[Fact]
	public void ARule_IsShownOncePerInterval()
	{
		TriggerSystemNotices notices = Notices();

		Assert.True(notices.Raise(_source, Errors, "ERROR 1"));
		_time.Advance(Interval - TimeSpan.FromMilliseconds(1));
		Assert.False(notices.Raise(_source, Errors, "ERROR 2"));

		_time.Advance(TimeSpan.FromMilliseconds(1));
		Assert.True(notices.Raise(_source, Errors, "ERROR 3"));
		Assert.Equal(2, _system.Shown.Count);
	}

	[Fact]
	public void TheLimit_IsPerRuleAndPerSession()
	{
		TriggerSystemNotices notices = Notices();
		TerminalTrigger done = new() { Id = Guid.NewGuid(), Pattern = "build succeeded", Notify = true };
		TriggerSource other = new(Guid.NewGuid(), null, "web-02");

		Assert.True(notices.Raise(_source, Errors, "ERROR"));
		Assert.True(notices.Raise(_source, done, "build succeeded"));
		Assert.True(notices.Raise(other, Errors, "ERROR"));
		Assert.False(notices.Raise(_source, Errors, "ERROR"));
	}

	// Taken even when the show failed, so a looping error does not ask the system again on every line.
	[Fact]
	public void AShowTheSystemRefused_StillUsesUpTheInterval()
	{
		TriggerSystemNotices notices = Notices();
		_system.Accepts = false;

		Assert.False(notices.Raise(_source, Errors, "ERROR"));
		_system.Accepts = true;
		Assert.False(notices.Raise(_source, Errors, "ERROR"));

		_time.Advance(Interval);
		Assert.True(notices.Raise(_source, Errors, "ERROR"));
	}

	[Fact]
	public void WithTheSettingOff_NothingIsShown()
	{
		_settings.Set(new TerminalTriggerSettings { SystemNotifications = false });

		Assert.False(Notices().Raise(_source, Errors, "ERROR"));
		Assert.Empty(_system.Shown);
	}

	[Fact]
	public void OnByDefault_WithTheLineOff()
	{
		TerminalTriggerSettings settings = new();

		Assert.True(settings.SystemNotifications);
		Assert.False(settings.SystemNotificationShowsLine);
	}

	[Fact]
	public void WhereTheHostHasNone_NothingIsShown()
	{
		TriggerSystemNotices notices = new(_settings, _time, NullLogger<TriggerSystemNotices>.Instance);

		Assert.False(notices.Raise(_source, Errors, "ERROR"));
	}

	[Fact]
	public void ARuleWithoutANotice_IsNeverShown()
	{
		Assert.False(Notices().Raise(_source, Errors with { Notify = false, Highlight = TriggerColor.Red }, "ERROR"));
		Assert.Empty(_system.Shown);
	}

	[Fact]
	public void AHostThatThrows_IsSwallowed()
	{
		_system.Throws = true;

		Assert.False(Notices().Raise(_source, Errors, "ERROR"));
	}

	[Fact]
	public void AQuickConnection_CarriesNoLogin()
	{
		FakeSession saved = new();
		FakeSession quick = new() { IsTransient = true };

		Assert.Equal(new TriggerSource(saved.Id, saved.Connection.Id, saved.Title), TriggerSource.From(saved));
		Assert.Null(TriggerSource.From(quick).ConnectionId);
	}

	[Fact]
	public void Records_PrintNoMachineAndNoLine()
	{
		SystemNotification notification = TriggerNotice.ForSystem(_source, Errors, "token=hunter2", showLine: true);

		string printed = notification.ToString() + _source;

		Assert.DoesNotContain("build-01", printed, StringComparison.Ordinal);
		Assert.DoesNotContain("hunter2", printed, StringComparison.Ordinal);
		Assert.Contains(_source.SessionId.ToString(), printed, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("", "Matched a trigger")]
	[InlineData("a\u202ebc", "Matched \"a bc\"")]
	[InlineData("x\ty", "Matched \"x y\"")]
	public void TheRule_IsNamedByItsPattern_OnOneLine(string pattern, string expected) =>
		Assert.Equal(expected, TriggerNotice.Rule(pattern));

	private TriggerSystemNotices Notices() => new(_settings, _time, NullLogger<TriggerSystemNotices>.Instance, _system);

	private sealed class FakeNotifications : ISystemNotifications
	{
		public List<SystemNotification> Shown { get; } = [];

		public bool Foreground { get; set; }

		public bool Accepts { get; set; } = true;

		public bool Throws { get; set; }

		public bool IsSessionWindowInForeground => Throws ? throw new InvalidOperationException("No window.") : Foreground;

		public bool Show(SystemNotification notification)
		{
			if (Accepts)
			{
				Shown.Add(notification);
			}

			return Accepts;
		}
	}
}
