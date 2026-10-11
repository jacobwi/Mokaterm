using System.Text;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// <c>x11-req</c> (RFC 4254 section 6.3.1): the authentication protocol, the cookie as hex text, which is what OpenSSH
/// sends and what <c>xauth</c> on the server takes, and the screen. No reply is asked for, for the same reason as
/// <see cref="AgentForwardingRequest"/>. SSH.NET has a class for this request but keeps it internal.
/// </summary>
internal sealed class X11ForwardingRequest : RequestInfo
{
	public const string Name = "x11-req";

	private readonly byte[] _protocol;
	private readonly byte[] _cookie;
	private readonly uint _screen;

	/// <param name="cookie">The fake cookie the server hands its programs. Only its hex form goes on the wire.</param>
	public X11ForwardingRequest(ReadOnlySpan<byte> cookie, int screen)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(screen);
		WantReply = false;
		_protocol = Encoding.ASCII.GetBytes(X11SetupRequest.MitMagicCookie);
		_cookie = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(cookie));
		_screen = (uint)screen;
	}

	public override string RequestName => Name;

	/// <summary>False: the fake cookie stays valid for every connection, as <c>ssh -X</c> leaves it.</summary>
	public static bool SingleConnection => false;

	protected override int BufferCapacity =>
		base.BufferCapacity + 1 + sizeof(uint) + _protocol.Length + sizeof(uint) + _cookie.Length + sizeof(uint);

	protected override void SaveData()
	{
		base.SaveData();
		Write(SingleConnection);
		WriteBinaryString(_protocol);
		WriteBinaryString(_cookie);
		Write(_screen);
	}
}
