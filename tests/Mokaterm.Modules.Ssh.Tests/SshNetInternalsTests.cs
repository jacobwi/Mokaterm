using System.Reflection;
using System.Text;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The parts of SSH.NET forwarding reaches without a public API. These fail loudly when an SSH.NET update moves them,
/// which is the signal to look at <see cref="SshNetInternals"/> again before anyone meets a session without forwarding.
/// </summary>
public sealed class SshNetInternalsTests
{
	private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

	[Fact]
	public void TheSessionAndItsSendPathAreReachable()
	{
		Assert.Null(SshNetInternals.SessionProblem);

		using SshClient client = new("127.0.0.1", "tester", "secret");
		Assert.Null(SshNetInternals.GetSession(client));

		Session session = SshNetSession.Create();
		Assert.NotNull(SshNetInternals.CreateSender(session));
	}

	[Fact]
	public void AnUnconnectedSession_RefusesToSendWithoutThrowing()
	{
		ISshMessageSender sender = SshNetInternals.CreateSender(SshNetSession.Create())!;

		Assert.False(sender.TrySend(new ChannelEofMessage(1)));
	}

	[Fact]
	public void StockChannelOpen_StillThrowsForAnAgentChannel()
	{
		// The reason the message table is changed at all. When this starts passing a channel through, the change can go.
		ChannelOpenMessage stock = new();

		_ = Assert.Throws<NotSupportedException>(() => stock.Load(SshNetSession.OpenBytes(SshForwarding.AgentChannelType, 3, 1000, 500, [])));
	}

	[Fact]
	public void TolerantChannelOpen_ReadsAnUnknownTypeWithoutThrowing()
	{
		TolerantChannelOpenMessage message = new();

		message.Load(SshNetSession.OpenBytes(SshForwarding.AgentChannelType, 3, 65536, 32768, []));

		Assert.True(message.IsUnknownType);
		Assert.Equal(SshForwarding.AgentChannelType, Encoding.ASCII.GetString(message.ChannelType));
		Assert.Equal(3u, message.LocalChannelNumber);
		Assert.Equal(65536u, message.InitialWindowSize);
		Assert.Equal(32768u, message.MaximumPacketSize);
		Assert.Null(message.Info);
	}

	[Fact]
	public void TolerantChannelOpen_ReadsKnownTypesAsBefore()
	{
		ChannelOpenMessage x11 = SshNetSession.X11Open(7);

		TolerantChannelOpenMessage message = new();
		message.Load(SshNetSession.OpenBytes(SshForwarding.X11ChannelType, 7, 1024, 512, [0, 0, 0, 9, .. "127.0.0.1"u8, 0, 0, 0x9C, 0x40]));

		Assert.False(message.IsUnknownType);
		Assert.Equal("X11ChannelOpenInfo", message.Info?.GetType().Name);
		Assert.Equal("X11ChannelOpenInfo", x11.Info?.GetType().Name);
	}

	[Fact]
	public void ChangedMessageTable_BuildsTolerantChannelOpens_AndKeepsThemThroughAKeyExchange()
	{
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());
		Assert.Null(SshNetInternals.UnknownChannelTypesProblem);

		// A second call changes nothing and reports the same.
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());

		Type factoryType = typeof(Session).Assembly.GetType("Renci.SshNet.SshMessageFactory", throwOnError: true)!;
		object factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
		Invoke(factory, "EnableAndActivateMessage", SshNetInternals.ChannelOpenMessageName);
		Assert.IsType<TolerantChannelOpenMessage>(Invoke(factory, "Create", (byte)90));

		// A key exchange empties the connection slots and refills them from the table; a mismatch there throws.
		Invoke(factory, "DisableNonKeyExchangeMessages", false);
		Invoke(factory, "EnableActivatedMessages");
		Assert.IsType<TolerantChannelOpenMessage>(Invoke(factory, "Create", (byte)90));

		Invoke(factory, "DisableNonKeyExchangeMessages", true);
		Invoke(factory, "EnableActivatedMessages");
		Message created = Assert.IsType<TolerantChannelOpenMessage>(Invoke(factory, "Create", (byte)90));
		Assert.Equal(90, created.MessageNumber);
	}

	[Fact]
	public void ChangedMessageTable_LeavesEveryOtherMessageAlone()
	{
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());

		Type factoryType = typeof(Session).Assembly.GetType("Renci.SshNet.SshMessageFactory", throwOnError: true)!;
		object factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
		Invoke(factory, "EnableAndActivateMessage", "SSH_MSG_CHANNEL_OPEN_CONFIRMATION");
		Invoke(factory, "EnableAndActivateMessage", "SSH_MSG_CHANNEL_DATA");

		Assert.IsType<ChannelOpenConfirmationMessage>(Invoke(factory, "Create", (byte)91));
		Assert.IsType<ChannelDataMessage>(Invoke(factory, "Create", (byte)94));
	}

	private static object? Invoke(object target, string name, params object[] arguments)
	{
		MethodInfo method = target.GetType().GetMethod(name, Hidden, [.. arguments.Select(argument => argument.GetType())])
			?? throw new InvalidOperationException($"SSH.NET no longer has SshMessageFactory.{name}.");
		return method.Invoke(target, arguments);
	}
}
