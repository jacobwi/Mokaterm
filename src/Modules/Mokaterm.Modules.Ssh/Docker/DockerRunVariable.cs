using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One environment variable for a new container. The value is where passwords go, so it is never printed.</summary>
internal sealed record DockerRunVariable(string Name, string Value)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Name = ").Append(Name).Append(", Value = ").Append(SecretText.Describe(Value));
		return true;
	}
}
