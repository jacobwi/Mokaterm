using System.Buffers.Binary;
using System.Text;
using Mokaterm.Modules.Ssh.Forwarding;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>The X11 connection setup, in both byte orders, built here byte by byte from the protocol's layout.</summary>
public sealed class X11SetupRequestTests
{
	private static readonly byte[] Cookie = [.. Enumerable.Range(1, 16).Select(value => (byte)value)];

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void TryRead_FindsTheAuthorization(bool bigEndian)
	{
		byte[] setup = Setup(bigEndian, "MIT-MAGIC-COOKIE-1", Cookie);

		Assert.Equal(X11SetupStatus.Complete, X11SetupRequest.TryRead(setup, out X11SetupRequest request));

		Assert.Equal(bigEndian, request.BigEndian);
		Assert.Equal(18, request.NameLength);
		Assert.Equal(16, request.DataLength);
		Assert.Equal(12 + 20 + 16, request.Length);
		Assert.Equal(setup.Length, request.Length);
		Assert.Equal("MIT-MAGIC-COOKIE-1", Encoding.ASCII.GetString(request.Name(setup)));
		Assert.Equal(Cookie, request.Data(setup).ToArray());
		Assert.True(request.Carries(setup, Cookie));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void TryRead_WaitsForTheWholeSetup(bool bigEndian)
	{
		byte[] setup = Setup(bigEndian, "MIT-MAGIC-COOKIE-1", Cookie);

		for (int length = 0; length < setup.Length; length++)
		{
			Assert.Equal(X11SetupStatus.NeedMore, X11SetupRequest.TryRead(setup.AsSpan(0, length), out _));
		}

		// Whatever follows the setup is the client's first request and is not part of it.
		byte[] withRequest = [.. setup, 55, 0, 2, 0];
		Assert.Equal(X11SetupStatus.Complete, X11SetupRequest.TryRead(withRequest, out X11SetupRequest request));
		Assert.Equal(setup.Length, request.Length);
	}

	[Theory]
	[InlineData((byte)0)]
	[InlineData((byte)'b')]
	[InlineData((byte)'L')]
	[InlineData((byte)0x16)]
	public void TryRead_RefusesAnythingButAByteOrder(byte first) =>
		Assert.Equal(X11SetupStatus.Invalid, X11SetupRequest.TryRead([first, 0, 11, 0], out _));

	[Fact]
	public void Carries_RefusesAnotherCookieOrProtocol()
	{
		byte[] wrongCookie = Setup(bigEndian: false, "MIT-MAGIC-COOKIE-1", [.. Cookie.Reverse()]);
		byte[] shortCookie = Setup(bigEndian: false, "MIT-MAGIC-COOKIE-1", Cookie[..15]);
		byte[] otherProtocol = Setup(bigEndian: false, "XDM-AUTHORIZATION-1", Cookie);
		byte[] none = Setup(bigEndian: false, "", []);

		foreach (byte[] setup in new[] { wrongCookie, shortCookie, otherProtocol, none })
		{
			Assert.Equal(X11SetupStatus.Complete, X11SetupRequest.TryRead(setup, out X11SetupRequest request));
			Assert.False(request.Carries(setup, Cookie));
		}
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void WithAuthorization_SwapsInTheRealCookie_InTheClientsByteOrder(bool bigEndian)
	{
		byte[] setup = Setup(bigEndian, "MIT-MAGIC-COOKIE-1", Cookie);
		byte[] real = [.. Enumerable.Range(100, 16).Select(value => (byte)value)];
		Assert.Equal(X11SetupStatus.Complete, X11SetupRequest.TryRead(setup, out X11SetupRequest request));

		byte[] rewritten = request.WithAuthorization(setup, "MIT-MAGIC-COOKIE-1", real);

		Assert.Equal(Setup(bigEndian, "MIT-MAGIC-COOKIE-1", real), rewritten);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void WithAuthorization_CanStripIt(bool bigEndian)
	{
		byte[] setup = Setup(bigEndian, "MIT-MAGIC-COOKIE-1", Cookie);
		Assert.Equal(X11SetupStatus.Complete, X11SetupRequest.TryRead(setup, out X11SetupRequest request));

		byte[] stripped = request.WithAuthorization(setup, "", []);

		Assert.Equal(Setup(bigEndian, "", []), stripped);
		Assert.Equal(X11SetupRequest.HeaderLength, stripped.Length);
		Assert.Equal(X11SetupStatus.Complete, X11SetupRequest.TryRead(stripped, out X11SetupRequest read));
		Assert.Equal((0, 0), (read.NameLength, read.DataLength));
	}

	[Fact]
	public void MaxLength_HoldsTheLongestSetup()
	{
		byte[] header = Setup(bigEndian: true, "", []);
		BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), ushort.MaxValue);
		BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), ushort.MaxValue);

		Assert.Equal(X11SetupStatus.NeedMore, X11SetupRequest.TryRead(header, out X11SetupRequest request));
		Assert.Equal(X11SetupRequest.MaxLength, request.Length);
	}

	/// <summary>A setup for protocol 11.0, as Xlib writes it.</summary>
	internal static byte[] Setup(bool bigEndian, string protocol, byte[] data)
	{
		byte[] name = Encoding.ASCII.GetBytes(protocol);
		int namePadded = (name.Length + 3) & ~3;
		int dataPadded = (data.Length + 3) & ~3;
		byte[] setup = new byte[12 + namePadded + dataPadded];
		setup[0] = bigEndian ? (byte)'B' : (byte)'l';
		Write(setup.AsSpan(2), 11);
		Write(setup.AsSpan(4), 0);
		Write(setup.AsSpan(6), name.Length);
		Write(setup.AsSpan(8), data.Length);
		name.CopyTo(setup, 12);
		data.CopyTo(setup, 12 + namePadded);
		return setup;

		void Write(Span<byte> target, int value)
		{
			if (bigEndian)
			{
				BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)value);
			}
			else
			{
				BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value);
			}
		}
	}
}
