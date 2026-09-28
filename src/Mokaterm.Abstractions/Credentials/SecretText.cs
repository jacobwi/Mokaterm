namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// What the records that carry a secret print for it: whether it is there, never what it is. A record's generated
/// ToString prints every property, and a record reaches a log line or an exception message far more easily than a
/// secret should, so such a record writes its own <c>PrintMembers</c> with this. Modules use it for their records too.
/// </summary>
public static class SecretText
{
	public static string Describe(string? secret) => secret switch
	{
		null => "null",
		"" => "(empty)",
		_ => "(set)",
	};
}
