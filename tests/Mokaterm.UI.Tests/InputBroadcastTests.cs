using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

public sealed class InputBroadcastTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void Toggle_TurnsASessionOnAndOffAgain()
	{
		using Harness harness = new();
		FakeSession session = harness.AddSession();

		Assert.True(harness.Broadcast.Toggle(session.Id));
		Assert.True(harness.Broadcast.IsMember(session.Id));
		Assert.False(harness.Broadcast.Toggle(session.Id));
		Assert.False(harness.Broadcast.IsMember(session.Id));
		Assert.Equal(0, harness.Broadcast.Count);
	}

	[Fact]
	public void OthersFor_OnItsOwn_IsEmpty()
	{
		using Harness harness = new();
		FakeSession alone = harness.AddSession();
		harness.Broadcast.Toggle(alone.Id);

		Assert.Empty(harness.Broadcast.OthersFor(alone.Id));
	}

	[Fact]
	public void OthersFor_SkipsTheSenderAndSessionsThatAreNotMembers()
	{
		using Harness harness = new();
		FakeSession sender = harness.AddSession();
		FakeSession partner = harness.AddSession();
		FakeSession bystander = harness.AddSession();
		harness.Broadcast.Toggle(sender.Id);
		harness.Broadcast.Toggle(partner.Id);

		IReadOnlyList<ITerminalStream> others = harness.Broadcast.OthersFor(sender.Id);

		Assert.Same(partner.Terminal, Assert.Single(others));
		Assert.Empty(harness.Broadcast.OthersFor(bystander.Id));
	}

	[Fact]
	public void OthersFor_SkipsAStreamThatIsClosed()
	{
		using Harness harness = new();
		FakeSession sender = harness.AddSession();
		FakeSession dropped = harness.AddSession();
		harness.Broadcast.Toggle(sender.Id);
		harness.Broadcast.Toggle(dropped.Id);
		((RecordingTerminalStream)dropped.Terminal!).IsOpen = false;

		Assert.Empty(harness.Broadcast.OthersFor(sender.Id));
	}

	[Fact]
	public void ClosingATab_TakesItOutOfTheGroup()
	{
		using Harness harness = new();
		FakeSession first = harness.AddSession();
		FakeSession second = harness.AddSession();
		harness.Broadcast.Toggle(first.Id);
		harness.Broadcast.Toggle(second.Id);

		harness.RemoveSession(second.Id);

		Assert.False(harness.Broadcast.IsMember(second.Id));
		Assert.Equal(1, harness.Broadcast.Count);
	}

	[Fact]
	public async Task Stream_RepeatsInputToTheOtherMembers()
	{
		using Harness harness = new();
		FakeSession sender = harness.AddSession();
		FakeSession partner = harness.AddSession();
		harness.Broadcast.Toggle(sender.Id);
		harness.Broadcast.Toggle(partner.Id);
		BroadcastTerminalStream stream = new((RecordingTerminalStream)sender.Terminal!, sender.Id, harness.Broadcast);

		await stream.SendTextAsync("uptime\r", Ct);
		await stream.SendAsync(Encoding.UTF8.GetBytes("q"), Ct);

		Assert.Equal(["uptime\r", "q"], ((RecordingTerminalStream)sender.Terminal!).Sent);
		Assert.Equal(["uptime\r", "q"], ((RecordingTerminalStream)partner.Terminal!).Sent);
	}

	[Fact]
	public async Task Stream_KeepsResizesAndLocalNoticesToItsOwnSession()
	{
		using Harness harness = new();
		FakeSession sender = harness.AddSession();
		FakeSession partner = harness.AddSession();
		harness.Broadcast.Toggle(sender.Id);
		harness.Broadcast.Toggle(partner.Id);
		BroadcastTerminalStream stream = new((RecordingTerminalStream)sender.Terminal!, sender.Id, harness.Broadcast);

		await stream.ResizeAsync(new TerminalSize(100, 40), Ct);
		await stream.WriteLocalAsync("[mokaterm] disconnected", Ct);

		Assert.Equal(new TerminalSize(100, 40), ((RecordingTerminalStream)sender.Terminal!).LastSize);
		Assert.Null(((RecordingTerminalStream)partner.Terminal!).LastSize);
		Assert.Empty(((RecordingTerminalStream)partner.Terminal!).Local);
	}

	[Fact]
	public async Task Stream_WithBroadcastOff_SendsOnlyToItsOwnSession()
	{
		using Harness harness = new();
		FakeSession sender = harness.AddSession();
		FakeSession other = harness.AddSession();
		BroadcastTerminalStream stream = new((RecordingTerminalStream)sender.Terminal!, sender.Id, harness.Broadcast);

		await stream.SendTextAsync("ls\r", Ct);

		Assert.Equal(["ls\r"], ((RecordingTerminalStream)sender.Terminal!).Sent);
		Assert.Empty(((RecordingTerminalStream)other.Terminal!).Sent);
	}

	private sealed class Harness : IDisposable
	{
		private readonly ManualSessionManager _manager = new();

		public Harness()
		{
			Workspace = new SessionWorkspace(_manager);
			Broadcast = new InputBroadcast(Workspace);
		}

		public SessionWorkspace Workspace { get; }

		public InputBroadcast Broadcast { get; }

		public FakeSession AddSession()
		{
			FakeSession session = new() { Terminal = new RecordingTerminalStream() };
			_manager.Add(session);
			return session;
		}

		public void RemoveSession(Guid sessionId) => _manager.Remove(sessionId);

		public void Dispose()
		{
			Broadcast.Dispose();
			Workspace.Dispose();
		}
	}

	}
