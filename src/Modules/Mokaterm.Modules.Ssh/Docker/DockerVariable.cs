using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One environment variable of a container. Values are where images take their passwords, so the page shows them
/// masked until asked, and the generated <c>ToString</c> never prints one.
/// </summary>
internal sealed record DockerVariable(string Name, string Value)
{
	/// <summary>Splits <c>NAME=value</c>; a variable without <c>=</c> is a name with no value.</summary>
	public static DockerVariable Parse(string entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		int equals = entry.IndexOf('=', StringComparison.Ordinal);
		return equals < 0 ? new DockerVariable(entry, "") : new DockerVariable(entry[..equals], entry[(equals + 1)..]);
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Name = ").Append(Name).Append(", Value = ").Append(SecretText.Describe(Value));
		return true;
	}
}
