using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One variable of an env file. <see cref="Value"/> is null for a bare <c>NAME</c> line, which in a service's env file
/// takes the value from the environment compose runs in. Values are where passwords live, so nothing prints one.
/// </summary>
/// <param name="Line">The 1-based line the variable starts on.</param>
internal sealed record DotEnvVariable(string Name, string? Value, int Line)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Name = ").Append(Name).Append(", Value = ").Append(SecretText.Describe(Value)).Append(", Line = ").Append(Line);
		return true;
	}
}
