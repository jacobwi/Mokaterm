using System.Globalization;

namespace Mokaterm.Modules.Rdp.Protocol;

internal static class RdpCredsspTrace
{
	public const string Waiting = "Login: waiting for the PC's answer";

	private static ReadOnlySpan<byte> NtlmSignature => "NTLMSSP\0"u8;

	// Kerberos 1.2.840.113554.1.2.2 and Microsoft's 1.2.840.48018.1.2.2 as DER object identifiers. SPNEGO lists both
	// even when its token is NTLM, which is why NTLM's own signature is looked for first.
	private static ReadOnlySpan<byte> KerberosOid => [0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x12, 0x01, 0x02, 0x02];

	private static ReadOnlySpan<byte> MicrosoftKerberosOid => [0x06, 0x09, 0x2A, 0x86, 0x48, 0x82, 0xF7, 0x12, 0x01, 0x02, 0x02];

	public static string Preparing(int number) =>
		string.Create(CultureInfo.InvariantCulture, $"Login: preparing message {number}");

	public static string Sent(int number, ReadOnlySpan<byte> message) => Mechanism(message) is { } mechanism
		? string.Create(CultureInfo.InvariantCulture, $"Login: sent message {number} ({mechanism}, {message.Length} bytes)")
		: string.Create(CultureInfo.InvariantCulture, $"Login: sent message {number} ({message.Length} bytes)");

	public static string Answered(int number, int length) =>
		string.Create(CultureInfo.InvariantCulture, $"Login: answer {number} from the PC ({length} bytes)");

	public static string? Mechanism(ReadOnlySpan<byte> message) =>
		message.IndexOf(NtlmSignature) >= 0 ? "NTLM"
		: message.IndexOf(KerberosOid) >= 0 || message.IndexOf(MicrosoftKerberosOid) >= 0 ? "Kerberos"
		: null;
}
