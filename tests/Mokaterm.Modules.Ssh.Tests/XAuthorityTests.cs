using System.Buffers.Binary;
using System.Text;
using Mokaterm.Modules.Ssh.Forwarding;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>Xauthority files written here in the format <c>xauth</c> uses: a family, then four counted strings, all big-endian.</summary>
public sealed class XAuthorityTests
{
	private const ushort Internet = 0;

	private const ushort Internet6 = 6;

	private const ushort Local = 256;

	private const ushort Wild = 65535;

	private static readonly byte[] First = [.. Enumerable.Repeat((byte)0x11, 16)];

	private static readonly byte[] Second = [.. Enumerable.Repeat((byte)0x22, 16)];

	[Fact]
	public void FindCookie_TakesTheLocalEntryOfThisMachine()
	{
		byte[] file =
		[
			.. Entry(Local, Encoding.ASCII.GetBytes("OTHERBOX"), "0", "MIT-MAGIC-COOKIE-1", Second),
			.. Entry(Local, Encoding.ASCII.GetBytes("devbox"), "0", "MIT-MAGIC-COOKIE-1", First),
		];

		Assert.Equal(First, XAuthority.FindCookie(file, X11Display.Default, "DEVBOX"));
	}

	[Fact]
	public void FindCookie_MatchesTheDisplayNumberAndProtocol()
	{
		byte[] file =
		[
			.. Entry(Local, Encoding.ASCII.GetBytes("devbox"), "1", "MIT-MAGIC-COOKIE-1", Second),
			.. Entry(Local, Encoding.ASCII.GetBytes("devbox"), "0", "XDM-AUTHORIZATION-1", Second),
			.. Entry(Local, Encoding.ASCII.GetBytes("devbox"), "0", "MIT-MAGIC-COOKIE-1", First),
		];

		Assert.Equal(First, XAuthority.FindCookie(file, X11Display.Default, "devbox"));
		Assert.Equal(Second, XAuthority.FindCookie(file, new X11Display("localhost", 1, 0), "devbox"));
		Assert.Null(XAuthority.FindCookie(file, new X11Display("localhost", 2, 0), "devbox"));
	}

	[Fact]
	public void FindCookie_TakesLoopbackAndWildcardEntriesForALocalDisplay()
	{
		byte[] loopback = Entry(Internet, [127, 0, 0, 1], "0", "MIT-MAGIC-COOKIE-1", First);
		byte[] loopback6 = Entry(Internet6, [.. new byte[15], 1], "0", "MIT-MAGIC-COOKIE-1", First);
		byte[] wild = Entry(Wild, [], "0", "MIT-MAGIC-COOKIE-1", Second);

		Assert.Equal(First, XAuthority.FindCookie(loopback, X11Display.Default, "devbox"));
		Assert.Equal(First, XAuthority.FindCookie(loopback6, X11Display.Default, "devbox"));
		Assert.Equal(Second, XAuthority.FindCookie(wild, new X11Display("10.0.0.5", 0, 0), "devbox"));
	}

	[Fact]
	public void FindCookie_KeepsRemoteDisplaysToTheirOwnAddress()
	{
		byte[] file =
		[
			.. Entry(Local, Encoding.ASCII.GetBytes("devbox"), "0", "MIT-MAGIC-COOKIE-1", First),
			.. Entry(Internet, [127, 0, 0, 1], "0", "MIT-MAGIC-COOKIE-1", First),
			.. Entry(Internet, [10, 0, 0, 5], "0", "MIT-MAGIC-COOKIE-1", Second),
		];

		Assert.Equal(Second, XAuthority.FindCookie(file, new X11Display("10.0.0.5", 0, 0), "devbox"));
		Assert.Null(XAuthority.FindCookie(file, new X11Display("10.0.0.6", 0, 0), "devbox"));
	}

	[Fact]
	public void FindCookie_StopsAtATruncatedRecord()
	{
		byte[] whole = Entry(Local, Encoding.ASCII.GetBytes("devbox"), "0", "MIT-MAGIC-COOKIE-1", First);

		for (int length = 0; length < whole.Length; length++)
		{
			Assert.Null(XAuthority.FindCookie(whole.AsSpan(0, length), X11Display.Default, "devbox"));
		}

		Assert.Null(XAuthority.FindCookie([0xFF, 0xFF, 0xFF, 0xFF, 0xFF], X11Display.Default, "devbox"));
	}

	private static byte[] Entry(ushort family, byte[] address, string number, string name, byte[] data)
	{
		List<byte> entry = [];
		Short(family);
		Counted(address);
		Counted(Encoding.ASCII.GetBytes(number));
		Counted(Encoding.ASCII.GetBytes(name));
		Counted(data);
		return [.. entry];

		void Short(int value)
		{
			byte[] bytes = new byte[2];
			BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
			entry.AddRange(bytes);
		}

		void Counted(byte[] value)
		{
			Short(value.Length);
			entry.AddRange(value);
		}
	}
}
