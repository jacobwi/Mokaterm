using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>One forwarded channel: both windows, the packet size, EOF and CLOSE, against a sender that keeps what is sent.</summary>
public sealed class SshForwardedChannelTests
{
	private const uint Local = 0xC000_0001;

	private const uint Remote = 9;

	[Fact]
	public void Confirm_AcceptsWithOurWindowAndPacket()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 100, windowSize: 4096, maxPacket: 1024);

		Assert.True(channel.Confirm());

		ChannelOpenConfirmationMessage confirmation = Assert.IsType<ChannelOpenConfirmationMessage>(Assert.Single(sender.Sent));
		Assert.Equal((Remote, Local, 4096u, 1024u), (confirmation.LocalChannelNumber, confirmation.RemoteChannelNumber, confirmation.InitialWindowSize, confirmation.MaximumPacketSize));
	}

	[Fact]
	public async Task Write_SplitsByTheServersPacketSize_AndWaitsForItsWindow()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 250, remoteMaxPacket: 100, windowSize: 4096, maxPacket: 1024);
		byte[] data = [.. Enumerable.Range(0, 400).Select(value => (byte)value)];

		Task<bool> writing = channel.WriteAsync(data, TestContext.Current.CancellationToken).AsTask();

		await sender.WaitForDataAsync(Remote, 250, "the first window's worth is sent");
		await Eventually.StaysTrueAsync(() => !writing.IsCompleted && sender.DataTo(Remote).Length == 250, "nothing more goes before the window opens");
		Assert.Equal([100, 100, 50], sender.OfType<ChannelDataMessage>().Select(message => message.Size));

		channel.OnWindowAdjust(1000);

		Assert.True(await writing.WaitAsync(Eventually.Limit, TestContext.Current.CancellationToken));
		Assert.Equal(data, sender.DataTo(Remote));
		Assert.All(sender.OfType<ChannelDataMessage>(), message => Assert.True(message.Size <= 100));
	}

	[Fact]
	public async Task Write_NeverSendsMoreThanOurOwnPacketSize()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1_000_000, remoteMaxPacket: 1_000_000, windowSize: 4096, maxPacket: 1024);

		Assert.True(await channel.WriteAsync(new byte[3000], TestContext.Current.CancellationToken));

		Assert.Equal([1024, 1024, 952], sender.OfType<ChannelDataMessage>().Select(message => message.Size));
	}

	[Fact]
	public async Task Read_ReopensTheWindowOnlyOnceHalfOfItWasRead()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 1000, windowSize: 1000, maxPacket: 1000);
		byte[] buffer = new byte[1000];

		channel.OnData(new byte[400]);
		Assert.Equal(400, await channel.ReadAsync(buffer, TestContext.Current.CancellationToken));
		Assert.Empty(sender.OfType<ChannelWindowAdjustMessage>());

		channel.OnData(new byte[300]);
		Assert.Equal(300, await channel.ReadAsync(buffer, TestContext.Current.CancellationToken));

		ChannelWindowAdjustMessage adjust = Assert.Single(sender.OfType<ChannelWindowAdjustMessage>());
		Assert.Equal((Remote, 700u), (adjust.LocalChannelNumber, adjust.BytesToAdd));
	}

	[Fact]
	public async Task Data_BeyondTheWindow_EndsTheChannel()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 1000, windowSize: 100, maxPacket: 100);

		channel.OnData(new byte[60]);
		channel.OnData(new byte[60]);

		await Eventually.CompletesAsync(channel.Closed);
		byte[] buffer = new byte[200];
		Assert.Equal(60, await channel.ReadAsync(buffer, TestContext.Current.CancellationToken));
		Assert.Equal(0, await channel.ReadAsync(buffer, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Eof_EndsReadsAfterWhatArrived()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 1000, windowSize: 1000, maxPacket: 1000);
		byte[] buffer = new byte[100];

		channel.OnData([1, 2, 3]);
		channel.OnEof();

		Assert.Equal(3, await channel.ReadAsync(buffer, TestContext.Current.CancellationToken));
		Assert.Equal(0, await channel.ReadAsync(buffer, TestContext.Current.CancellationToken));
		Assert.False(channel.Closed.IsCompleted);
	}

	[Fact]
	public async Task Close_SendsEofThenCloseOnce_AndNothingAfter()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 1000, windowSize: 1000, maxPacket: 1000);

		channel.Close();
		channel.Close();
		channel.SendEof();

		Assert.False(await channel.WriteAsync(new byte[10], TestContext.Current.CancellationToken));
		Assert.Collection(
			sender.Sent,
			message => Assert.Equal(Remote, Assert.IsType<ChannelEofMessage>(message).LocalChannelNumber),
			message => Assert.Equal(Remote, Assert.IsType<ChannelCloseMessage>(message).LocalChannelNumber));
		Assert.True(channel.Closed.IsCompleted);
	}

	[Fact]
	public async Task RemoteClose_StopsAWriterWaitingForTheWindow()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 0, remoteMaxPacket: 1000, windowSize: 1000, maxPacket: 1000);

		ValueTask<bool> writing = channel.WriteAsync(new byte[10], TestContext.Current.CancellationToken);
		channel.OnClose();

		Assert.False(await writing);
		Assert.Empty(sender.OfType<ChannelDataMessage>());
	}

	[Fact]
	public async Task Request_ThatWantsAnAnswer_GetsAFailure()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 1000, windowSize: 1000, maxPacket: 1000);

		channel.OnRequest(wantReply: false);
		channel.OnRequest(wantReply: true);

		ChannelFailureMessage failure = await sender.WaitForAsync<ChannelFailureMessage>(_ => true, "the request is answered");
		Assert.Equal(Remote, failure.LocalChannelNumber);
		Assert.Single(sender.OfType<Message>());
	}

	[Fact]
	public async Task LostConnection_EndsTheChannelWithoutSending()
	{
		RecordingMessageSender sender = new();
		SshForwardedChannel channel = new(sender, Local, Remote, remoteWindow: 1000, remoteMaxPacket: 1000, windowSize: 1000, maxPacket: 1000);

		channel.OnConnectionLost();
		channel.Close();

		await Eventually.CompletesAsync(channel.Closed);
		Assert.Empty(sender.Sent);
	}
}
