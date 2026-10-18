using Mokaterm.Abstractions.Sessions;
using Mokaterm.Maui.Services;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// What a Windows notification carries back to the app: the desktop host's argument format, compiled in here because the
/// host targets net10.0-windows. The system keeps the arguments after the app has gone, so they are ids and nothing else.
/// </summary>
public sealed class NotificationClickTests
{
	private static readonly Guid Session = Guid.Parse("6d1f0c2e-58a4-4c61-9d7b-0e2f4a9b3c11");
	private static readonly Guid Connection = Guid.Parse("a3b4c5d6-e7f8-4a1b-8c2d-3e4f5a6b7c8d");

	[Fact]
	public void Arguments_AreTheIdsAlone()
	{
		SystemNotification notification = new()
		{
			Title = "Trigger in bc@build-01.example.com",
			Message = "Matched \"ERROR\"",
			Detail = "ERROR: token=hunter2",
			SessionId = Session,
			ConnectionId = Connection,
		};

		IReadOnlyList<KeyValuePair<string, string>> arguments = NotificationClick.ArgumentsFor(notification);

		Assert.Equal(
			[
				new(NotificationClick.SessionKey, "6d1f0c2e58a44c619d7b0e2f4a9b3c11"),
				new(NotificationClick.ConnectionKey, "a3b4c5d6e7f84a1b8c2d3e4f5a6b7c8d"),
			],
			arguments);
		Assert.All(arguments, argument => Assert.Matches("^[0-9a-f]{32}$", argument.Value));
	}

	[Fact]
	public void Arguments_WithoutALogin_CarryTheSessionOnly()
	{
		SystemNotification notification = new() { Title = "Trigger", Message = "Matched \"x\"", SessionId = Session };

		Assert.Equal([NotificationClick.SessionKey], NotificationClick.ArgumentsFor(notification).Select(argument => argument.Key));
	}

	[Fact]
	public void Read_GivesBackWhatWasWritten()
	{
		SystemNotification notification = new() { Title = "t", Message = "m", SessionId = Session, ConnectionId = Connection };

		Assert.Equal(new NotificationClick(Session, Connection), NotificationClick.Read(NotificationClick.ArgumentsFor(notification)));
	}

	// Any program can start the app with arguments of its choosing.
	[Fact]
	public void Read_PassesOverAnythingButOurIdsInOurForm()
	{
		NotificationClick click = NotificationClick.Read(
		[
			new("session", "{6d1f0c2e-58a4-4c61-9d7b-0e2f4a9b3c11}"),
			new("connection", "not an id"),
			new("host", "a3b4c5d6e7f84a1b8c2d3e4f5a6b7c8d"),
			new("Session", "a3b4c5d6e7f84a1b8c2d3e4f5a6b7c8d"),
		]);

		Assert.Equal(new NotificationClick(null, null), click);
		Assert.Equal(new NotificationClick(null, null), NotificationClick.Read(null));
	}

	[Fact]
	public void TheSessionShown_IsTheOneTheNotificationWasAbout()
	{
		FakeSession other = new();
		FakeSession about = new();

		Assert.Equal(about.Id, new NotificationClick(about.Id, other.Connection.Id).SessionToShow([other, about]));
	}

	// The session closed and its login was opened again, under a new id.
	[Fact]
	public void TheSessionShown_ElseIsAnOpenSessionOfTheSameLogin()
	{
		FakeSession reopened = new(Connection);

		Assert.Equal(reopened.Id, new NotificationClick(Session, Connection).SessionToShow([new FakeSession(), reopened]));
	}

	[Fact]
	public void TheSessionShown_IsNone_WhenNothingMatches()
	{
		FakeSession quick = new(Connection) { IsTransient = true };

		Assert.Null(new NotificationClick(Session, Connection).SessionToShow([quick, new FakeSession()]));
		Assert.Null(new NotificationClick(null, null).SessionToShow([quick]));
	}
}
