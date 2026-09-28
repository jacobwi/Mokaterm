using System.Globalization;
using System.Text;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>The twelve byte <c>RFB 003.008\n</c> greeting both sides start with.</summary>
internal readonly record struct RfbProtocolVersion(int Major, int Minor)
{
	public const int Length = 12;

	/// <summary>The newest version this module speaks.</summary>
	public static RfbProtocolVersion Latest { get; } = new(3, 8);

	public bool IsAtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);

	/// <summary>
	/// Reads a greeting. Versions servers use to mean 3.8 (Apple's 003.889, Intel AMT's 004.000, RealVNC's 004.001
	/// and 005.000) are reported as 3.8, the same way every other client treats them.
	/// </summary>
	public static bool TryParse(ReadOnlySpan<byte> greeting, out RfbProtocolVersion version)
	{
		version = default;
		if (greeting.Length != Length || !greeting[..4].SequenceEqual("RFB "u8) || greeting[7] != (byte)'.' || greeting[11] != (byte)'\n')
		{
			return false;
		}

		if (!int.TryParse(Encoding.ASCII.GetString(greeting[4..7]), NumberStyles.None, CultureInfo.InvariantCulture, out int major)
			|| !int.TryParse(Encoding.ASCII.GetString(greeting[8..11]), NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
		{
			return false;
		}

		version = (major, minor) switch
		{
			(3, 889) => Latest,
			(4, _) or (5, _) => Latest,
			_ => new RfbProtocolVersion(major, minor),
		};

		return true;
	}

	/// <summary>The greeting bytes for this version.</summary>
	public byte[] ToBytes() =>
		Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"RFB {Major:000}.{Minor:000}\n"));

	public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");
}
