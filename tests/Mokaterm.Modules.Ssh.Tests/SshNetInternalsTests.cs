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
	public void KeepAliveRequest_IsOpenSshsKeepAlive_AndWantsAReply()
	{
		Assert.Null(SshNetInternals.KeepAliveProblem);

		GlobalRequestMessage message = SshNetInternals.CreateKeepAliveRequest()!;

		Assert.Equal(SshNetInternals.KeepAliveRequestName, message.RequestName);
		Assert.True(message.WantReply);

		// SSH_MSG_GLOBAL_REQUEST, string "keepalive@openssh.com", boolean want_reply (RFC 4254 section 4).
		byte[] expected = [80, 0, 0, 0, 21, .. "keepalive@openssh.com"u8, 1];
		Assert.Equal(expected, message.GetBytes());

		// Each call builds a message of its own, so two writes never share one.
		Assert.NotSame(message, SshNetInternals.CreateKeepAliveRequest());
	}

	[Fact]
	public void SshNetsOwnGlobalRequests_AreStillOnlyTheRemoteForwardOnes()
	{
		// GlobalRequestLine counts every global request that wants a reply. When SSH.NET gains another one of its own, it
		// has to be counted there too, or a keepalive reply could pass for its answer.
		string[] requests = [.. typeof(Session).Assembly.GetTypes()
			.Where(type => type.IsSubclassOf(typeof(GlobalRequestMessage)))
			.Select(type => type.Name)
			.Order(StringComparer.Ordinal)];

		Assert.Equal(["CancelTcpIpForwardGlobalRequestMessage", "TcpIpForwardGlobalRequestMessage"], requests);
	}

	[Fact]
	public void ARemoteForward_StillWaitsOnOneHandleForAnyReply()
	{
		// Why forward starts wait until nothing is owed: the remote forward takes whatever reply comes next for its own,
		// through one auto-reset handle that stays subscribed while the port runs. When this stops holding, look at
		// GlobalRequestLine again; it stays correct either way, only stricter than needed.
		FieldInfo? response = typeof(ForwardedPortRemote).GetField("_globalRequestResponse", Hidden);
		Assert.NotNull(response);
		using ForwardedPortRemote port = new("127.0.0.1", 8080, "127.0.0.1", 80);
		Assert.IsType<AutoResetEvent>(response.GetValue(port));
		Assert.NotNull(typeof(ForwardedPortRemote).GetMethod("Session_RequestSuccess", Hidden));
		Assert.NotNull(typeof(ForwardedPortRemote).GetMethod("Session_RequestFailure", Hidden));
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
