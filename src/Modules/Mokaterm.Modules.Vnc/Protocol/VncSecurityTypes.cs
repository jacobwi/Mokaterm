using System.Globalization;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>What each security type needs and how it is shown to the user.</summary>
internal static class VncSecurityTypes
{
	/// <summary>True when the session itself runs inside TLS.</summary>
	public static bool IsEncrypted(VncSecurityType type) => type
		is VncSecurityType.TlsNone or VncSecurityType.TlsVnc or VncSecurityType.TlsPlain
		or VncSecurityType.X509None or VncSecurityType.X509Vnc or VncSecurityType.X509Plain;

	/// <summary>True when the server proves who it is with a certificate instead of anonymous key exchange.</summary>
	public static bool UsesCertificate(VncSecurityType type) => type
		is VncSecurityType.X509None or VncSecurityType.X509Vnc or VncSecurityType.X509Plain;

	/// <summary>True when the type ends in the DES challenge of classic VNC authentication.</summary>
	public static bool NeedsPassword(VncSecurityType type) => type
		is VncSecurityType.VncAuth or VncSecurityType.TlsVnc or VncSecurityType.X509Vnc;

	/// <summary>True when the type ends in a user name and password sent as plain text inside the tunnel.</summary>
	public static bool NeedsUsername(VncSecurityType type) => type
		is VncSecurityType.Plain or VncSecurityType.TlsPlain or VncSecurityType.X509Plain;

	/// <summary>Short label for the session view, for example <c>VNC password over TLS (certificate)</c>.</summary>
	public static string Describe(VncSecurityType type) => type switch
	{
		VncSecurityType.None => "No authentication, unencrypted",
		VncSecurityType.VncAuth => "VNC password, unencrypted",
		VncSecurityType.Plain => "User name and password, unencrypted",
		VncSecurityType.TlsNone => "No authentication over anonymous TLS",
		VncSecurityType.TlsVnc => "VNC password over anonymous TLS",
		VncSecurityType.TlsPlain => "User name and password over anonymous TLS",
		VncSecurityType.X509None => "No authentication over TLS (certificate)",
		VncSecurityType.X509Vnc => "VNC password over TLS (certificate)",
		VncSecurityType.X509Plain => "User name and password over TLS (certificate)",
		_ => string.Create(CultureInfo.InvariantCulture, $"Security type {(int)type}"),
	};

	/// <summary>The name used in error messages for a type the module cannot use.</summary>
	public static string Name(int type) => Enum.IsDefined((VncSecurityType)type)
		? ((VncSecurityType)type).ToString()
		: string.Create(CultureInfo.InvariantCulture, $"{type}");
}
