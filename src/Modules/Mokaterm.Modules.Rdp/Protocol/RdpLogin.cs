using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>The three parts a Windows login is made of, after the user name has been taken apart.</summary>
/// <param name="Domain">The domain or machine name, empty for a local account.</param>
/// <param name="Username">The account name on its own.</param>
/// <param name="Password">Empty when the server is meant to ask for itself.</param>
internal readonly record struct RdpLogin(string Domain, string Username, string Password)
{
	/// <summary>
	/// Splits the forms people type into a name and a domain. <c>CORP\abc</c> and <c>abc@corp.example</c> both
	/// carry their own domain, and one written in the connection's options wins over neither.
	/// </summary>
	public static RdpLogin Parse(string? username, string? domain, string? password)
	{
		string name = (username ?? "").Trim();
		string scope = (domain ?? "").Trim();
		if (scope.Length == 0)
		{
			int separator = name.IndexOf('\\', StringComparison.Ordinal);
			if (separator > 0)
			{
				scope = name[..separator];
				name = name[(separator + 1)..];
			}
		}

		// A user principal name carries its domain in a form the server parses itself, so it stays whole.
		return new RdpLogin(scope, name, password ?? "");
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
