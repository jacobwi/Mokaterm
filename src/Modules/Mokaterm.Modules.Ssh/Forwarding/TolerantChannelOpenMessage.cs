using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// SSH.NET's channel open message, except that a channel type SSH.NET has no class for is read instead of thrown on.
/// The stock message reads every field and only then throws <see cref="NotSupportedException"/> for an unknown type,
/// which SSH.NET's message loop turns into the end of the connection. Here the fields stay read and
/// <see cref="ChannelOpenMessage.Info"/> stays null, which SSH.NET's own listeners already skip, so whoever asked for the
/// channel type can answer it.
/// </summary>
internal sealed class TolerantChannelOpenMessage : ChannelOpenMessage
{
	/// <summary>True when the channel type was one SSH.NET has no class for.</summary>
	public bool IsUnknownType { get; private set; }

	protected override void LoadData()
	{
		try
		{
			base.LoadData();
		}
		catch (NotSupportedException)
		{
			IsUnknownType = true;
		}
	}
}
