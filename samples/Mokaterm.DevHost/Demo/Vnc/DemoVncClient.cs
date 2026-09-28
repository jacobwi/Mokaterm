using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.DevHost.Demo.Vnc;

/// <summary>
/// One viewer of the demo screen: the server side of the RFB handshake with VNC authentication, then the message
/// loop that answers framebuffer requests with Raw rectangles and feeds input into the screen.
/// </summary>
internal sealed class DemoVncClient : IAsyncDisposable
{
	private const byte MessageSetPixelFormat = 0;
	private const byte MessageSetEncodings = 2;
	private const byte MessageFramebufferUpdateRequest = 3;
	private const byte MessageKeyEvent = 4;
	private const byte MessagePointerEvent = 5;
	private const byte MessageClientCutText = 6;

	private const int MaxCutTextLength = 64 * 1024;

	private readonly TcpClient _client;
	private readonly Stream _stream;
	private readonly DemoScreen _screen;
	private readonly string _password;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly SemaphoreSlim _pending = new(0);
	private readonly Lock _gate = new();
	private DemoRect _damage;
	private bool _updateRequested;
	private DemoPixelFormat _format = DemoPixelFormat.Default;

	public DemoVncClient(TcpClient client, DemoScreen screen, string password, ILogger logger)
	{
		_client = client;
		_client.NoDelay = true;
		_stream = client.GetStream();
		_screen = screen;
		_password = password;
		_logger = logger;
	}

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		if (!await HandshakeAsync(cancellationToken))
		{
			return;
		}

		using CancellationTokenSource stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_screen.Damaged += OnDamaged;
		Task sender = SendUpdatesAsync(stopped.Token);
		try
		{
			await ReadMessagesAsync(stopped.Token);
		}
		finally
		{
			_screen.Damaged -= OnDamaged;
			await stopped.CancelAsync();
			await sender;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stream.DisposeAsync();
		_client.Dispose();
		_writeLock.Dispose();
		_pending.Dispose();
	}

	private async Task<bool> HandshakeAsync(CancellationToken cancellationToken)
	{
		await WriteAsync(Encoding.ASCII.GetBytes("RFB 003.008\n"), cancellationToken);
		byte[] version = await ReadAsync(12, cancellationToken);
		if (!RfbProtocolVersion.TryParse(version, out RfbProtocolVersion _))
		{
			_logger.LogWarning("A demo VNC viewer greeted with {Greeting}", Encoding.ASCII.GetString(version).Trim());
			return false;
		}

		// One security type: VNC authentication, so the workbench exercises the DES challenge.
		await WriteAsync([1, (byte)VncSecurityType.VncAuth], cancellationToken);
		byte chosen = (await ReadAsync(1, cancellationToken))[0];
		if (chosen != (byte)VncSecurityType.VncAuth)
		{
			return false;
		}

		byte[] challenge = RandomNumberGenerator.GetBytes(VncPasswordAuthentication.ChallengeLength);
		await WriteAsync(challenge, cancellationToken);
		byte[] answer = await ReadAsync(VncPasswordAuthentication.ChallengeLength, cancellationToken);
		byte[] expected = VncPasswordAuthentication.CreateResponse(challenge, Encoding.UTF8.GetBytes(_password));
		if (!CryptographicOperations.FixedTimeEquals(answer, expected))
		{
			await WriteAsync([0, 0, 0, 1], cancellationToken);
			await WriteReasonAsync("The demo password did not match.", cancellationToken);
			_logger.LogInformation("A demo VNC viewer sent the wrong password.");
			return false;
		}

		await WriteAsync([0, 0, 0, 0], cancellationToken);
		await ReadAsync(1, cancellationToken);
		await WriteAsync(BuildServerInit(), cancellationToken);
		return true;
	}

	private static byte[] BuildServerInit()
	{
		byte[] name = Encoding.UTF8.GetBytes(DemoScreen.DesktopName);
		byte[] message = new byte[24 + name.Length];
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0, 2), DemoScreen.Width);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), DemoScreen.Height);
		message[4] = 32;
		message[5] = 24;
		message[7] = 1;
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), 255);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(10, 2), 255);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(12, 2), 255);
		message[14] = 16;
		message[15] = 8;
		message[16] = 0;
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(20, 4), (uint)name.Length);
		name.CopyTo(message.AsSpan(24));
		return message;
	}

	private void OnDamaged(DemoRect rect)
	{
		lock (_gate)
		{
			_damage = _damage.Union(rect);
			if (!_updateRequested)
			{
				return;
			}
		}

		_pending.Release();
	}

	private async Task ReadMessagesAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			byte type = (await ReadAsync(1, cancellationToken))[0];
			switch (type)
			{
				case MessageSetPixelFormat:
					byte[] pixelFormat = await ReadAsync(19, cancellationToken);
					_format = DemoPixelFormat.Parse(pixelFormat.AsSpan(3));
					break;

				case MessageSetEncodings:
					byte[] header = await ReadAsync(3, cancellationToken);
					int count = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1, 2));

					// The demo only ever sends Raw, so the list itself does not matter.
					await ReadAsync(count * 4, cancellationToken);
					break;

				case MessageFramebufferUpdateRequest:
					byte[] request = await ReadAsync(9, cancellationToken);
					RequestUpdate(request[0] != 0);
					break;

				case MessageKeyEvent:
					byte[] key = await ReadAsync(7, cancellationToken);
					if (key[0] != 0)
					{
						_screen.ShowKey(DemoKeys.Describe(BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(3, 4))));
					}

					break;

				case MessagePointerEvent:
					byte[] pointer = await ReadAsync(5, cancellationToken);
					_screen.MovePointer(
						BinaryPrimitives.ReadUInt16BigEndian(pointer.AsSpan(1, 2)),
						BinaryPrimitives.ReadUInt16BigEndian(pointer.AsSpan(3, 2)),
						pointer[0] != 0);
					break;

				case MessageClientCutText:
					byte[] cutText = await ReadAsync(7, cancellationToken);
					uint declared = BinaryPrimitives.ReadUInt32BigEndian(cutText.AsSpan(3, 4));
					int length = (int)Math.Min(declared, MaxCutTextLength);
					byte[] text = await ReadAsync(length, cancellationToken);

					// Whatever is left of an oversized message still has to leave the stream.
					await SkipAsync(declared - (uint)length, cancellationToken);
					await HandleCutTextAsync(Encoding.Latin1.GetString(text), cancellationToken);
					break;

				default:
					_logger.LogWarning("A demo VNC viewer sent message type {Type}, which the demo server does not know.", type);
					return;
			}
		}
	}

	private void RequestUpdate(bool incremental)
	{
		lock (_gate)
		{
			_updateRequested = true;
			if (!incremental)
			{
				_damage = new DemoRect(0, 0, DemoScreen.Width, DemoScreen.Height);
			}

			if (_damage.IsEmpty)
			{
				return;
			}
		}

		_pending.Release();
	}

	private async Task SendUpdatesAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await _pending.WaitAsync(cancellationToken);
				DemoRect rect;
				lock (_gate)
				{
					rect = _damage;
					_damage = default;
					_updateRequested = false;
				}

				if (!rect.IsEmpty)
				{
					await SendRectangleAsync(rect, cancellationToken);
				}
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
		{
			// The viewer went away.
		}
	}

	private async Task SendRectangleAsync(DemoRect rect, CancellationToken cancellationToken)
	{
		int bytesPerPixel = _format.BytesPerPixel;
		byte[] message = new byte[16 + (rect.Width * rect.Height * bytesPerPixel)];
		message[0] = 0;
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), 1);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4, 2), (ushort)rect.X);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6, 2), (ushort)rect.Y);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), (ushort)rect.Width);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(10, 2), (ushort)rect.Height);
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(12, 4), 0);

		uint[] pixels = new uint[rect.Width * rect.Height];
		_screen.Read(rect, pixels);
		for (int i = 0; i < pixels.Length; i++)
		{
			_format.Write(pixels[i], message.AsSpan(16 + (i * bytesPerPixel), bytesPerPixel));
		}

		await WriteAsync(message, cancellationToken);
	}

	private async Task HandleCutTextAsync(string text, CancellationToken cancellationToken)
	{
		_screen.SetClipboard(text);

		// Echoed back so the view's "copy from the session" button has something to offer.
		byte[] body = Encoding.Latin1.GetBytes("demo: " + text);
		byte[] message = new byte[8 + body.Length];
		message[0] = 3;
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4, 4), (uint)body.Length);
		body.CopyTo(message.AsSpan(8));
		await WriteAsync(message, cancellationToken);
	}

	private async Task WriteReasonAsync(string reason, CancellationToken cancellationToken)
	{
		byte[] text = Encoding.UTF8.GetBytes(reason);
		byte[] message = new byte[4 + text.Length];
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(0, 4), (uint)text.Length);
		text.CopyTo(message.AsSpan(4));
		await WriteAsync(message, cancellationToken);
	}

	private async Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[count];
		await _stream.ReadExactlyAsync(buffer, cancellationToken);
		return buffer;
	}

	private async Task SkipAsync(uint count, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[Math.Min(count, 8 * 1024)];
		while (count > 0)
		{
			int take = (int)Math.Min(count, (uint)buffer.Length);
			await _stream.ReadExactlyAsync(buffer.AsMemory(0, take), cancellationToken);
			count -= (uint)take;
		}
	}

	private async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
	{
		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			await _stream.WriteAsync(data, cancellationToken);
			await _stream.FlushAsync(cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}
	}
}
