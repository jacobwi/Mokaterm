using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Mokaterm.Plugins.Sample.Subnets;

/// <summary>
/// Works out an IPv4 or IPv6 prefix from what was typed: <c>192.168.1.10/24</c>, <c>10.0.0.0/255.0.0.0</c>,
/// <c>2001:db8::/48</c>, or an address alone, which is its own /32 or /128. Addresses are 128-bit numbers here, an IPv4
/// one in the low 32 bits, so both families share the arithmetic.
/// </summary>
internal static partial class SubnetMath
{
	/// <summary>Works out <paramref name="text"/>, or says why it cannot be read.</summary>
	public static bool TryCalculate(string? text, [NotNullWhen(true)] out SubnetInfo? info, [NotNullWhen(false)] out string? problem)
	{
		info = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type an address with a prefix, such as 192.168.1.10/24 or 2001:db8::/48.";
			return false;
		}

		int slash = input.IndexOf('/', StringComparison.Ordinal);
		string host = slash < 0 ? input : input[..slash].Trim();
		if (!TryParseAddress(host, out IPAddress? address))
		{
			problem = $"{host} is not an IPv4 or IPv6 address.";
			return false;
		}

		int bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
		int prefix = bits;
		if (slash >= 0 && !TryParsePrefix(input[(slash + 1)..].Trim(), address.AddressFamily, bits, out prefix, out problem))
		{
			return false;
		}

		info = Calculate(address, prefix);
		problem = null;
		return true;
	}

	/// <summary>The prefix of <paramref name="prefixLength"/> bits that holds <paramref name="address"/>.</summary>
	public static SubnetInfo Calculate(IPAddress address, int prefixLength)
	{
		ArgumentNullException.ThrowIfNull(address);
		bool v4 = address.AddressFamily == AddressFamily.InterNetwork;
		int bits = v4 ? 32 : 128;
		ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, bits);

		UInt128 all = v4 ? uint.MaxValue : UInt128.MaxValue;
		UInt128 hostMask = prefixLength == bits ? UInt128.Zero : all >> prefixLength;
		UInt128 mask = all & ~hostMask;
		UInt128 value = ToNumber(address);
		UInt128 network = value & mask;
		UInt128 last = network | hostMask;
		BigInteger count = BigInteger.One << (bits - prefixLength);

		// RFC 3021 gives both addresses of an IPv4 /31 to hosts, and a /32 is the host itself; below those the first and
		// last address of an IPv4 network are not hosts. IPv6 has no broadcast address.
		bool reserved = v4 && prefixLength < 31;
		return new SubnetInfo
		{
			Address = address,
			PrefixLength = prefixLength,
			Network = ToAddress(network, v4),
			Mask = ToAddress(mask, v4),
			Wildcard = v4 ? ToAddress(hostMask, v4) : null,
			Broadcast = reserved ? ToAddress(last, v4) : null,
			FirstHost = ToAddress(reserved ? network + UInt128.One : network, v4),
			LastHost = ToAddress(reserved ? last - UInt128.One : last, v4),
			AddressCount = count,
			HostCount = reserved ? count - 2 : count,
		};
	}

	/// <summary>
	/// An IPv4 address as four dotted numbers, or an IPv6 address. <see cref="IPAddress.TryParse(string, out IPAddress)"/>
	/// alone also reads <c>10.1</c> or <c>167772161</c> as IPv4, which nobody typing a subnet means.
	/// </summary>
	private static bool TryParseAddress(string text, [NotNullWhen(true)] out IPAddress? address)
	{
		address = null;
		if (!IPAddress.TryParse(text, out IPAddress? parsed))
		{
			return false;
		}

		bool shaped = parsed.AddressFamily switch
		{
			AddressFamily.InterNetwork => DottedPattern().IsMatch(text),
			AddressFamily.InterNetworkV6 => text.Contains(':', StringComparison.Ordinal),
			_ => false,
		};

		if (!shaped)
		{
			return false;
		}

		// A zone such as %eth0 names a link, not a part of the prefix.
		address = parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.ScopeId != 0 ? new IPAddress(parsed.GetAddressBytes()) : parsed;
		return true;
	}

	/// <summary>A prefix length, or for IPv4 a dotted mask whose set bits all come first.</summary>
	private static bool TryParsePrefix(string text, AddressFamily family, int bits, out int prefix, [NotNullWhen(false)] out string? problem)
	{
		prefix = 0;
		if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int length))
		{
			if (length <= bits)
			{
				prefix = length;
				problem = null;
				return true;
			}

			problem = $"An {(bits == 32 ? "IPv4" : "IPv6")} prefix is 0 to {bits} bits long.";
			return false;
		}

		if (family == AddressFamily.InterNetwork && DottedPattern().IsMatch(text) && IPAddress.TryParse(text, out IPAddress? mask))
		{
			uint value = (uint)ToNumber(mask);
			int ones = BitOperations.PopCount(value);
			if (value == (ones == 0 ? 0u : uint.MaxValue << (32 - ones)))
			{
				prefix = ones;
				problem = null;
				return true;
			}

			problem = $"{text} is not a mask: its set bits have to come first, as in 255.255.255.0.";
			return false;
		}

		problem = $"{text} is not a prefix length.";
		return false;
	}

	private static UInt128 ToNumber(IPAddress address)
	{
		UInt128 value = UInt128.Zero;
		foreach (byte part in address.GetAddressBytes())
		{
			value = (value << 8) | part;
		}

		return value;
	}

	private static IPAddress ToAddress(UInt128 value, bool v4)
	{
		byte[] bytes = new byte[v4 ? 4 : 16];
		for (int index = bytes.Length - 1; index >= 0; index--)
		{
			bytes[index] = (byte)value;
			value >>= 8;
		}

		return new IPAddress(bytes);
	}

	[GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}\z", RegexOptions.CultureInvariant)]
	private static partial Regex DottedPattern();
}
