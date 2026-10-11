using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Finds the real MIT-MAGIC-COOKIE-1 for a display in an Xauthority file, which is what <c>ssh -X</c> asks
/// <c>xauth</c> for. Windows X servers rarely have one, and then forwarding strips the authorization instead, which works
/// because they usually run with access control off or allow 127.0.0.1.
/// </summary>
/// <remarks>
/// The file is a run of records, each a 16-bit family and four 16-bit length strings (address, display number, protocol
/// name, data), all big-endian. A file larger than <see cref="MaxFileBytes"/> or one that ends mid-record is read up to
/// where it stops making sense.
/// </remarks>
internal static class XAuthority
{
	public const string FileVariable = "XAUTHORITY";

	public const int MaxFileBytes = 1024 * 1024;

	private const ushort FamilyInternet = 0;

	private const ushort FamilyInternet6 = 6;

	private const ushort FamilyLocalHost = 252;

	private const ushort FamilyLocal = 256;

	private const ushort FamilyWild = 65535;

	private const string FileName = ".Xauthority";

	/// <summary>The cookie for <paramref name="display"/> from the user's Xauthority file, or null when there is none.</summary>
	public static byte[]? FindCookie(X11Display display)
	{
		if (FindFile() is not { } path)
		{
			return null;
		}

		try
		{
			FileInfo file = new(path);
			if (!file.Exists || file.Length > MaxFileBytes)
			{
				return null;
			}

			return FindCookie(File.ReadAllBytes(path), display, Environment.MachineName);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
		{
			return null;
		}
	}

	/// <summary>
	/// The first MIT-MAGIC-COOKIE-1 in <paramref name="file"/> that belongs to <paramref name="display"/>: a wildcard
	/// entry, or for a display on this machine one recorded under <paramref name="machineName"/> or the loopback address
	/// (Xlib files a TCP connection to localhost under the machine's name), or one under the display's own address.
	/// </summary>
	public static byte[]? FindCookie(ReadOnlySpan<byte> file, X11Display display, string machineName)
	{
		byte[] number = Encoding.ASCII.GetBytes(display.Number.ToString(CultureInfo.InvariantCulture));
		IPAddress? literal = IPAddress.TryParse(display.Host, out IPAddress? parsed) ? parsed : null;
		int position = 0;
		while (TryReadRecord(file, ref position, out ushort family, out Range address, out Range recordNumber, out Range name, out Range data))
		{
			if (!file[name].SequenceEqual("MIT-MAGIC-COOKIE-1"u8)
				|| !file[recordNumber].SequenceEqual(number)
				|| file[data].IsEmpty)
			{
				continue;
			}

			if (Matches(family, file[address], display, literal, machineName))
			{
				return file[data].ToArray();
			}
		}

		return null;
	}

	private static bool Matches(ushort family, ReadOnlySpan<byte> address, X11Display display, IPAddress? literal, string machineName) => family switch
	{
		FamilyWild => true,
		FamilyLocal => display.IsLocal && string.Equals(Encoding.ASCII.GetString(address), machineName, StringComparison.OrdinalIgnoreCase),
		FamilyLocalHost => display.IsLocal,
		FamilyInternet => address.Length == 4 && SameAddress(address, display, literal, AddressFamily.InterNetwork, IPAddress.Loopback),
		FamilyInternet6 => address.Length == 16 && SameAddress(address, display, literal, AddressFamily.InterNetworkV6, IPAddress.IPv6Loopback),
		_ => false,
	};

	private static bool SameAddress(ReadOnlySpan<byte> address, X11Display display, IPAddress? literal, AddressFamily addressFamily, IPAddress loopback)
	{
		IPAddress recorded = new(address);
		if (literal is not null)
		{
			return literal.AddressFamily == addressFamily && literal.Equals(recorded);
		}

		return display.IsLocal && recorded.Equals(loopback);
	}

	private static bool TryReadRecord(
		ReadOnlySpan<byte> file,
		ref int position,
		out ushort family,
		out Range address,
		out Range number,
		out Range name,
		out Range data)
	{
		address = number = name = data = default;
		family = 0;
		int at = position;
		if (!TryReadShort(file, ref at, out family)
			|| !TryReadCounted(file, ref at, out address)
			|| !TryReadCounted(file, ref at, out number)
			|| !TryReadCounted(file, ref at, out name)
			|| !TryReadCounted(file, ref at, out data))
		{
			return false;
		}

		position = at;
		return true;
	}

	private static bool TryReadShort(ReadOnlySpan<byte> file, ref int position, out ushort value)
	{
		value = 0;
		if (file.Length - position < sizeof(ushort))
		{
			return false;
		}

		value = BinaryPrimitives.ReadUInt16BigEndian(file[position..]);
		position += sizeof(ushort);
		return true;
	}

	private static bool TryReadCounted(ReadOnlySpan<byte> file, ref int position, out Range value)
	{
		value = default;
		if (!TryReadShort(file, ref position, out ushort length) || file.Length - position < length)
		{
			return false;
		}

		value = new Range(position, position + length);
		position += length;
		return true;
	}

	private static string? FindFile()
	{
		if (Environment.GetEnvironmentVariable(FileVariable) is { Length: > 0 } configured)
		{
			return configured;
		}

		string? home = Environment.GetEnvironmentVariable("HOME");
		if (string.IsNullOrEmpty(home))
		{
			home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		return string.IsNullOrEmpty(home) ? null : Path.Combine(home, FileName);
	}
}
