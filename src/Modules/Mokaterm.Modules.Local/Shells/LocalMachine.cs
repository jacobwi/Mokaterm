using System.Collections;

namespace Mokaterm.Modules.Local.Shells;

/// <summary>
/// The parts of this computer that finding and starting a shell reads: its environment, its files and what the
/// registry lists. Everything is a value or a function, so the rules above it are tested against a machine described
/// in the test rather than the one running it.
/// </summary>
/// <remarks>
/// A class and not a record: the environment can hold a token, and a record would print it from <c>ToString</c>.
/// </remarks>
internal sealed class LocalMachine
{
	/// <summary>The environment a shell inherits, by name in any case, as Windows looks names up.</summary>
	public required IReadOnlyDictionary<string, string> Variables { get; init; }

	/// <summary>The user's profile folder, where a shell starts when nothing names a folder.</summary>
	public required string UserProfile { get; init; }

	/// <summary>This computer's name, which a host's address may use to mean "the default shell here".</summary>
	public required string MachineName { get; init; }

	public Func<string, bool> FileExists { get; init; } = File.Exists;

	public Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;

	public Func<IReadOnlyList<string>> GitInstallFolders { get; init; } = static () => [];

	public Func<IReadOnlyList<WslDistribution>> WslDistributions { get; init; } = static () => [];

	/// <summary>This computer as it is now. Read again for every connect, so a shell installed meanwhile is found.</summary>
	public static LocalMachine Current()
	{
		Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);
		foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
		{
			if (entry.Key is string name && entry.Value is string value)
			{
				variables[name] = value;
			}
		}

		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (profile.Length == 0 && variables.TryGetValue("USERPROFILE", out string? fromVariable))
		{
			profile = fromVariable;
		}

		return new LocalMachine
		{
			Variables = variables,
			UserProfile = profile,
			MachineName = Environment.MachineName,
			GitInstallFolders = WindowsShellRegistry.ReadGitInstallFolders,
			WslDistributions = WindowsShellRegistry.ReadWslDistributions,
		};
	}

	public string? Variable(string name) => Variables.TryGetValue(name, out string? value) && value.Length > 0 ? value : null;
}
