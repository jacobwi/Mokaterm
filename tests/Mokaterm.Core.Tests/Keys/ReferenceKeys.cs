namespace Mokaterm.Core.Tests.Keys;

/// <summary>
/// Throwaway keys written by OpenSSH 10.3 <c>ssh-keygen</c>. They protect nothing. The public keys and
/// fingerprints are what <c>ssh-keygen</c> itself produced, so the tests check Mokaterm against OpenSSH rather
/// than against its own writer.
/// </summary>
internal static class ReferenceKeys
{
	public const string Passphrase = "pass phrase";

	public const string Ed25519 = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
		QyNTUxOQAAACBKZtltUu1d+BqNDmvwrszEG8L1dBmg9K2Pm7OdICBh1QAAAJh7kngWe5J4
		FgAAAAtzc2gtZWQyNTUxOQAAACBKZtltUu1d+BqNDmvwrszEG8L1dBmg9K2Pm7OdICBh1Q
		AAAEBCHlLv62aaokSQClnwZqyIK6g3B4vmvXqLWmWIBodTtEpm2W1S7V34Go0Oa/CuzMQb
		wvV0GaD0rY+bs50gIGHVAAAAEm1va2F0ZXJtLXJlZmVyZW5jZQECAw==
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string Ed25519PublicKey =
		"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEpm2W1S7V34Go0Oa/CuzMQbwvV0GaD0rY+bs50gIGHV mokaterm-reference";

	public const string Ed25519Fingerprint = "SHA256:V1w9knIBXshEdUdIfZlSk9j7/ki3ljdN1kclzWPwrsw";

	public const string EncryptedEd25519 = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAACmFlczI1Ni1jdHIAAAAGYmNyeXB0AAAAGAAAABA9eKBGXC
		VWBIWv6FrUDNQ/AAAABAAAAAEAAAAzAAAAC3NzaC1lZDI1NTE5AAAAIH3U7OOkOhAHjMa0
		g30yIzWVhouHfDnJDfHasfNoqMokAAAAoACcMQn8A3GRHb1tt4yYmGwlHkynpMuwFc4271
		sWQkYPvNtWDtQZfG6YbfdXn6bvXV7AW+ukljsD0BRQKJzDe1tovU/b5hDbi3lHiCBG9OjI
		1w2h1+VJ21EE1FB5P7TdFNOyDgpEG3dC6FGK+UNtls+UiRjA0v0f9u3/8lhmgp+5Kp2B5E
		emytVfa7QggFXgxax3vzV/umVDqe64GtbmHVQ=
		-----END OPENSSH PRIVATE KEY-----
		""";

	/// <summary>The same line without a comment: a comment lives in the encrypted half of the file.</summary>
	public const string EncryptedEd25519PublicKey =
		"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIH3U7OOkOhAHjMa0g30yIzWVhouHfDnJDfHasfNoqMok";
}
