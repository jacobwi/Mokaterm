namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// Picks the security type to use from what a server offers. Encrypted types win over unencrypted ones, a
/// certificate wins over anonymous key exchange, and an authenticated type wins over none at all.
/// </summary>
internal static class VncSecuritySelection
{
	/// <summary>
	/// Chooses the type to answer the server's list with. VeNCrypt wins whenever it is offered because its subtypes
	/// are the only way to get TLS. Returns <see cref="VncSecurityType.Invalid"/> when nothing is usable.
	/// </summary>
	public static VncSecurityType ChooseTopLevel(IEnumerable<int> offered, VncEncryptionMode encryption)
	{
		ArgumentNullException.ThrowIfNull(offered);
		bool veNCrypt = false;
		bool vncAuth = false;
		bool none = false;
		foreach (int type in offered)
		{
			veNCrypt |= type == (int)VncSecurityType.VeNCrypt;
			vncAuth |= type == (int)VncSecurityType.VncAuth;
			none |= type == (int)VncSecurityType.None;
		}

		if (veNCrypt)
		{
			return VncSecurityType.VeNCrypt;
		}

		if (encryption == VncEncryptionMode.Required)
		{
			return VncSecurityType.Invalid;
		}

		return vncAuth ? VncSecurityType.VncAuth : none ? VncSecurityType.None : VncSecurityType.Invalid;
	}

	/// <summary>
	/// Chooses a VeNCrypt subtype. <paramref name="hasUsername"/> tips the balance towards the Plain subtypes, which
	/// are the ones that can carry a user name.
	/// </summary>
	public static VncSecurityType ChooseSubtype(IEnumerable<int> offered, VncEncryptionMode encryption, bool hasUsername)
	{
		ArgumentNullException.ThrowIfNull(offered);
		VncSecurityType best = VncSecurityType.Invalid;
		int bestScore = 0;
		foreach (int number in offered)
		{
			VncSecurityType type = (VncSecurityType)number;
			if (!IsSupportedSubtype(type) || (encryption == VncEncryptionMode.Required && !VncSecurityTypes.IsEncrypted(type)))
			{
				continue;
			}

			int score = Score(type, hasUsername);
			if (score > bestScore)
			{
				bestScore = score;
				best = type;
			}
		}

		return best;
	}

	private static bool IsSupportedSubtype(VncSecurityType type) => type
		is VncSecurityType.None or VncSecurityType.VncAuth or VncSecurityType.Plain
		or VncSecurityType.TlsNone or VncSecurityType.TlsVnc or VncSecurityType.TlsPlain
		or VncSecurityType.X509None or VncSecurityType.X509Vnc or VncSecurityType.X509Plain;

	private static int Score(VncSecurityType type, bool hasUsername)
	{
		int transport = VncSecurityTypes.IsEncrypted(type) ? (VncSecurityTypes.UsesCertificate(type) ? 200 : 100) : 0;
		int authentication = VncSecurityTypes.NeedsUsername(type)
			? (hasUsername ? 3 : 2)
			: VncSecurityTypes.NeedsPassword(type) ? (hasUsername ? 2 : 3) : 1;
		return transport + authentication;
	}
}
