using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local;

/// <summary>Typed access to the <c>local.*</c> keys a local shell login keeps in its <see cref="ProtocolOptions"/>.</summary>
/// <remarks>
/// Every value is an override, so a login carries only what makes it different: no shell runs what the host's address
/// names, and no folder starts where Settings says. Values are kept as typed, because the editor reports every
/// keystroke and trimming one would take the space the user just typed; whoever reads a value trims it.
/// </remarks>
public sealed record LocalConnectionOptions
{
	/// <summary>Every key this module owns starts with this.</summary>
	public const string KeyPrefix = "local.";

	public const string ShellKey = "local.shell";

	public const string ProgramKey = "local.program";

	public const string ArgumentsKey = "local.arguments";

	public const string FolderKey = "local.folder";

	public const string EnvironmentKey = "local.environment";

	public static LocalConnectionOptions Default { get; } = new();

	/// <summary>
	/// The shell this login runs: <c>pwsh</c>, <c>powershell</c>, <c>cmd</c>, <c>git-bash</c>, <c>wsl:</c> followed by
	/// a distribution's name, or <c>custom</c> for <see cref="Program"/>. Null runs the shell the host's address names.
	/// </summary>
	public string? Shell { get; init; }

	/// <summary>The program a <c>custom</c> shell runs: a full path, or the name of a program on PATH.</summary>
	public string? Program { get; init; }

	/// <summary>Command line text added after the arguments the shell is started with, passed on as typed.</summary>
	public string? Arguments { get; init; }

	/// <summary>The folder the shell starts in. Null takes the one in Settings.</summary>
	public string? StartingFolder { get; init; }

	/// <summary><c>NAME=value</c> lines added to the shell's environment. <c>NAME=</c> with nothing after it removes one.</summary>
	public string? EnvironmentVariables { get; init; }

	/// <summary>True when the login picked a program of its own rather than one of the shells found here.</summary>
	public bool IsCustom => string.Equals(Shell?.Trim(), LocalShellIds.Custom, StringComparison.OrdinalIgnoreCase);

	public static LocalConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new LocalConnectionOptions
		{
			Shell = Kept(options.GetString(ShellKey)),
			Program = Kept(options.GetString(ProgramKey)),
			Arguments = Kept(options.GetString(ArgumentsKey)),
			StartingFolder = Kept(options.GetString(FolderKey)),
			EnvironmentVariables = Kept(options.GetString(EnvironmentKey)),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options
			.With(ShellKey, Kept(Shell))
			.With(ProgramKey, Kept(Program))
			.With(ArgumentsKey, Kept(Arguments))
			.With(FolderKey, Kept(StartingFolder))
			.With(EnvironmentKey, Kept(EnvironmentVariables));
	}

	/// <summary>
	/// The program as a path: trimmed, and without the quotes Explorer's "Copy as path" puts around it. The quotes are
	/// not part of the name, and the command line quotes the path itself when it runs.
	/// </summary>
	internal static string? ProgramPath(string? program)
	{
		string trimmed = program?.Trim() ?? "";
		if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
		{
			trimmed = trimmed[1..^1].Trim();
		}

		return trimmed.Length == 0 ? null : trimmed;
	}

	private static string? Kept(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// Arguments and environment variables are where a token ends up, so they say whether they are set and no more.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"Shell = {Shell}, Program = {Program}, Arguments = {SecretText.Describe(Arguments)}, StartingFolder = {StartingFolder}, EnvironmentVariables = {SecretText.Describe(EnvironmentVariables)}");
		return true;
	}
}
