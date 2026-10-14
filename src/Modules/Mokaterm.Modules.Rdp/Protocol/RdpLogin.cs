using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>The three parts a Windows login is made of, after the user name has been taken apart.</summary>
/// <param name="Domain">The domain or machine name, empty for a local account or a principal name.</param>
/// <param name="Username">The account name on its own, or a whole principal name such as <c>you@example.com</c>.</param>
/// <param name="Password">Empty when the server is meant to ask for itself.</param>
/// <remarks>
/// CredSSP runs in sspi-rs inside IronRDP, and its NTLM sends these forms (checked against a fake NTLM server in the
/// tests): <c>alice</c> as user <c>alice</c> with no domain, <c>CORP\alice</c> as <c>alice</c> in <c>CORP</c>, and a
/// principal name <c>alice@corp.example</c> split into <c>alice</c> in <c>corp.example</c>, where Windows' own client
/// and FreeRDP send the whole principal name with no domain. It has no form at all for a domain together with an
/// <c>@</c> in the name, which is how a Microsoft account is written (<c>MicrosoftAccount\you@example.com</c>).
/// </remarks>
internal readonly record struct RdpLogin(string Domain, string Username, string Password)
{
	/// <summary>
	/// Splits the forms people type into a name and a domain. <c>CORP\abc</c> carries its own domain, which wins over
	/// one written in the connection's options; otherwise the options' domain applies. A principal name stays whole.
	/// </summary>
	public static RdpLogin Parse(string? username, string? domain, string? password)
	{
		string name = (username ?? "").Trim();
		string scope = (domain ?? "").Trim();
		int separator = name.IndexOf('\\', StringComparison.Ordinal);
		if (separator > 0)
		{
			scope = name[..separator].Trim();
			name = name[(separator + 1)..].Trim();
		}

		return new RdpLogin(scope, name, password ?? "");
	}

	/// <summary>True for a principal name with no domain, which is how people often type a Microsoft account.</summary>
	public bool IsBarePrincipalName => Domain.Length == 0 && Username.Contains('@', StringComparison.Ordinal);

	/// <summary>
	/// Why CredSSP cannot send this login, or null when it can. sspi-rs refuses a domain together with a backslash or an
	/// <c>@</c> in either half ("mixed username format"), and only after the connection, the TLS handshake and the
	/// certificate question, so the connector asks this first.
	/// </summary>
	public string? CredsspRefusal
	{
		get
		{
			if (Domain.Length == 0 || (Username.IndexOfAny(['\\', '@']) < 0 && Domain.IndexOfAny(['\\', '@']) < 0))
			{
				return null;
			}

			string written = $"{Domain}\\{Username}";
			return Username.Contains('@', StringComparison.Ordinal)
				? $"Network level authentication in this client cannot send {written}: it has no form for a domain together " +
					"with an @ in the name. For a Microsoft account, run whoami on that PC and use the name it prints (such " +
					"as DESKTOP-ABC\\name) with the account's password, not its PIN."
				: $"{written} is not a user name Windows can read. Write it as name, DOMAIN\\name or name@domain.";
		}
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Domain = ").Append(Domain)
			.Append(", Username = ").Append(Username)
			.Append(", Password = ").Append(SecretText.Describe(Password));
		return true;
	}
}
