using Mokaterm.Modules.Ssh.Forwarding;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>Keeps every message forwarding sends, in order, instead of writing it to a connection.</summary>
internal sealed class RecordingMessageSender : ISshMessageSender
{
	private readonly List<Message> _sent = [];

	/// <summary>False makes every send fail, the way a connection that went away does.</summary>
	public bool Connected { get; set; } = true;

	public IReadOnlyList<Message> Sent
	{
		get
		{
			lock (_sent)
			{
				return [.. _sent];
			}
		}
	}

	public bool TrySend(Message message)
	{
		if (!Connected)
		{
			return false;
		}

		lock (_sent)
		{
			_sent.Add(message);
		}

		return true;
	}

	public IReadOnlyList<T> OfType<T>()
		where T : Message =>
		[.. Sent.OfType<T>()];

	public async Task<T> WaitForAsync<T>(Func<T, bool> match, string description)
		where T : Message
	{
		T? found = null;
		await Eventually.TrueAsync(() => (found = OfType<T>().FirstOrDefault(match)) is not null, description);
		return found!;
	}

	/// <summary>Everything sent as data to <paramref name="remoteChannel"/>, joined.</summary>
	public byte[] DataTo(uint remoteChannel) =>
		[.. OfType<ChannelDataMessage>()
			.Where(message => message.LocalChannelNumber == remoteChannel)
			.SelectMany(message => message.Data.AsSpan(message.Offset, message.Size).ToArray())];

	public Task WaitForDataAsync(uint remoteChannel, int length, string description) =>
		Eventually.TrueAsync(() => DataTo(remoteChannel).Length >= length, description);
}
