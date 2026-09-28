namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// RFB security types, VeNCrypt subtypes included. The subtype numbers above 255 only appear inside a
/// <see cref="VeNCrypt"/> negotiation and never on the wire as a plain security type.
/// </summary>
internal enum VncSecurityType
{
	Invalid = 0,
	None = 1,
	VncAuth = 2,
	Ra2 = 5,
	Ra2ne = 6,
	Tight = 16,
	VeNCrypt = 19,
	Ard = 30,
	MsLogonII = 113,

	Plain = 256,
	TlsNone = 257,
	TlsVnc = 258,
	TlsPlain = 259,
	X509None = 260,
	X509Vnc = 261,
	X509Plain = 262,
}
