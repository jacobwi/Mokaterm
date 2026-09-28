using System.Text;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Sessions;

/// <summary>
/// The handshake the page side gets instead of the real one: RFB 003.008, one security type (None), an OK security
/// result and, once the page sends its ClientInit, the ServerInit the server really sent. The fourteen handshake
/// bytes the page produces are answered here and never reach the server, which is already past its own handshake.
/// </summary>
internal sealed class VncPageHandshake
{
	private static readonly byte[] SecurityTypeList = [1, (byte)VncSecurityType.None];

	private static readonly byte[] SecurityResultOk = [0, 0, 0, 0];

	private readonly byte[] _serverInit;
	private readonly byte[] _version = new byte[RfbProtocolVersion.Length];
	private int _received;
	private Stage _stage;

	public VncPageHandshake(byte[] serverInit) => _serverInit = serverInit;

	private enum Stage
	{
		Version,
		SecurityType,
		ClientInit,
		Relay,
	}

	public bool IsComplete => _stage == Stage.Relay;

	/// <summary>The greeting the page must see before it says anything.</summary>
	public static byte[] Greeting() => RfbProtocolVersion.Latest.ToBytes();

	/// <summary>
	/// Takes bytes from the page, adds the answers it earned to <paramref name="replies"/> and returns what is left
	/// for the server.
	/// </summary>
	/// <exception cref="VncProtocolException">The page did not follow the handshake it was given.</exception>
	public ReadOnlyMemory<byte> Consume(ReadOnlyMemory<byte> data, ICollection<byte[]> replies)
	{
		ArgumentNullException.ThrowIfNull(replies);
		while (!IsComplete && !data.IsEmpty)
		{
			switch (_stage)
			{
				case Stage.Version:
					int take = Math.Min(_version.Length - _received, data.Length);
					data.Span[..take].CopyTo(_version.AsSpan(_received));
					_received += take;
					data = data[take..];
					if (_received == _version.Length)
					{
						CheckVersion();
						replies.Add(SecurityTypeList);
						_stage = Stage.SecurityType;
					}

					break;

				case Stage.SecurityType:
					byte type = data.Span[0];
					data = data[1..];
					if (type != (byte)VncSecurityType.None)
					{
						throw new VncProtocolException($"The page picked security type {type} although only None was offered.");
					}

					replies.Add(SecurityResultOk);
					_stage = Stage.ClientInit;
					break;

				default:
					// The shared flag: the real one was decided by the connection's options and already sent.
					data = data[1..];
					replies.Add(_serverInit);
					_stage = Stage.Relay;
					break;
			}
		}

		return data;
	}

	private void CheckVersion()
	{
		if (!RfbProtocolVersion.TryParse(_version, out RfbProtocolVersion version) || !version.IsAtLeast(3, 8))
		{
			throw new VncProtocolException($"The page answered with an unexpected RFB version: {Encoding.ASCII.GetString(_version).TrimEnd('\n')}");
		}
	}
}
