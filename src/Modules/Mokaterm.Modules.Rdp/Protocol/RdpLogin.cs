using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>The three parts a Windows login is made of, after the user name has been taken apart.</summary>
/// <param name="Domain">The domain or machine name, empty for a local account or a principal name.</param>
/// <param name="Username">The account name on its own, or a whole principal name such as <c>you@example.com</c>.</param>
/// <param name="Password">Empty when the server is meant to ask for itself.</param>
/// <remarks>
/// <para>
/// The same two names go out on every route, because IronRDP takes one domain and one user name for both CredSSP and
/// the Client Info PDU.
/// </para>
/// <para>
/// CredSSP runs in sspi-rs inside IronRDP, and its NTLM sends these forms (checked against a fake NTLM server in the
/// tests): <c>alice</c> as user <c>alice</c> with no domain, <c>CORP\alice</c> as <c>alice</c> in <c>CORP</c>, and a
/// principal name <c>alice@corp.example</c> split into <c>alice</c> in <c>corp.example</c>, where Windows' own client
/// and FreeRDP send the whole principal name with no domain. It has no form at all for a domain together with an
/// <c>@</c> in the name, which is how a Microsoft account is written (<c>MicrosoftAccount\you@example.com</c>).
/// </para>
/// <para>
/// The Client Info PDU (MS-RDPBCGR 2.2.1.11.1.1, TS_INFO_PACKET) carries Domain and UserName as plain UTF-16 strings,
/// written as given (checked against a fake server in the tests). This split is FreeRDP's (<c>freerdp_parse_username_ptr</c>
/// in <c>client/common/cmdline.c</c>), which cuts at the first backslash only and keeps a principal name whole with an
/// empty domain, the form its comment says both CredSSP and the Client Info PDU expect, and it is what Microsoft
/// documents for Entra ID with network level authentication off (<c>AzureAD\</c> and the principal name):
/// <c>AzureAD\you@company.com</c> is <c>AzureAD</c> and <c>you@company.com</c>, <c>MicrosoftAccount\you@outlook.com</c>
/// is <c>MicrosoftAccount</c> and <c>you@outlook.com</c>, <c>CORP\alice</c> is <c>CORP</c> and <c>alice</c>,
/// <c>alice@corp.example</c> is an empty domain and <c>alice@corp.example</c>, <c>.\alice</c> is <c>.</c> (this PC's own
/// accounts) and <c>alice</c>, and a bare <c>alice</c> is an empty domain and <c>alice</c>. A name without a backslash
/// takes the domain in the connection's options, if there is one.
/// </para>
/// </remarks>
internal readonly record struct RdpLogin(string Domain, string Username, string Password)
{
	/// <summary>The domain Windows writes a Microsoft Entra ID account under.</summary>
	public const string EntraIdDomain = "AzureAD";

	/// <summary>The domain Windows writes a Microsoft account under.</summary>
	public const string MicrosoftAccountDomain = "MicrosoftAccount";

	private const string TurnOffNla =
		"turn off \"Require devices to use Network Level Authentication to connect\" on that PC (Settings > System > Remote " +
		"Desktop) and \"Network level authentication\" in this login's RDP options. The login then signs in by itself.";

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

	/// <summary>True for a Microsoft Entra ID account, <c>AzureAD\you@company.com</c>.</summary>
	public bool IsEntraId => Domain.Equals(EntraIdDomain, StringComparison.OrdinalIgnoreCase);

	/// <summary>True for a Microsoft account written with its domain, <c>MicrosoftAccount\you@outlook.com</c>.</summary>
	public bool IsMicrosoftAccount => Domain.Equals(MicrosoftAccountDomain, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// What works for an Entra ID or Microsoft account, which network level authentication from here cannot sign in, or
	/// null for any other login. An Entra ID account needs PKU2U or Entra's web sign-in (PROTOCOL_RDSAAD) for it, which
	/// neither IronRDP nor sspi-rs has; with network level authentication off the login goes in the Client Info PDU,
	/// which Windows signs in with as it does for mstsc.
	/// </summary>
	public string? CloudAccountAdvice
	{
		get
		{
			if (IsEntraId)
			{
				string email = Username.Contains('@', StringComparison.Ordinal)
					? ""
					: "It also signs in with its full work email, written AzureAD\\you@company.com. ";
				return "A Microsoft Entra ID account cannot pass network level authentication from a PC that is not joined to " +
					"Entra ID. " + email + "To sign in with it, " + TurnOffNla;
			}

			return IsMicrosoftAccount
				? "Network level authentication from this client does not work for a Microsoft account. To sign in with it, " +
					TurnOffNla + " Or keep network level authentication, run whoami on that PC and use the name it prints " +
					"(such as DESKTOP-ABC\\name). Either way, use the account's password, not its PIN."
				: null;
		}
	}

	/// <summary>
	/// Why CredSSP cannot send this login, or null when it can. sspi-rs refuses a domain together with a backslash or an
	/// <c>@</c> in either half ("mixed username format"), and only after the connection, the TLS handshake and the
	/// certificate question, so the connector asks this first. An Entra ID account is refused whatever its form.
	/// </summary>
	public string? CredsspRefusal
	{
		get
		{
			if (IsEntraId || (IsMicrosoftAccount && Username.Contains('@', StringComparison.Ordinal)))
			{
				return CloudAccountAdvice;
			}

			if (Domain.Length == 0 || (Username.IndexOfAny(['\\', '@']) < 0 && Domain.IndexOfAny(['\\', '@']) < 0))
			{
				return null;
			}

			string written = $"{Domain}\\{Username}";
			return Username.Contains('@', StringComparison.Ordinal) && Domain.IndexOfAny(['\\', '@']) < 0
				? $"Network level authentication in this client cannot send {written}: it has no form for a domain together " +
					"with an @ in the name. Leave the domain empty for a name with an @ in it, or write it as DOMAIN\\name."
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
