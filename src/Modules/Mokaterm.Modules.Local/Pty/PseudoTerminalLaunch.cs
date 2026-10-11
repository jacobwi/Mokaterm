using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Local.Pty;

/// <summary>
/// The program a pseudo terminal starts, in a form no platform owns: the arguments stay a list, and the text a login
/// typed stays text, because Windows takes one command line and a Unix pty would split that text the shell's way.
/// </summary>
internal sealed record PseudoTerminalLaunch
{
	/// <summary>The full path of the program.</summary>
	public required string Program { get; init; }

	/// <summary>Arguments this module chose, each one quoted as the platform needs.</summary>
	public IReadOnlyList<string> Arguments { get; init; } = [];

	/// <summary>Command line text the login typed, added after <see cref="Arguments"/> exactly as it is.</summary>
	public string? ExtraArguments { get; init; }

	public required string WorkingDirectory { get; init; }

	/// <summary>The program's whole environment. Nothing of this process's own is added on the way.</summary>
	public required IReadOnlyDictionary<string, string> Environment { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// The typed text and the environment can each hold a token, so neither is printed.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"Program = {Program}, Arguments = {Arguments.Count}, ExtraArguments = {SecretText.Describe(ExtraArguments)}, WorkingDirectory = {WorkingDirectory}, Environment = {Environment.Count} variables");
		return true;
	}
}
