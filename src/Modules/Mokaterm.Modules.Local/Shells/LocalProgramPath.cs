namespace Mokaterm.Modules.Local.Shells;

/// <summary>Finds the program a name stands for, the way a shell would, minus the parts a shell gets wrong for us.</summary>
internal static class LocalProgramPath
{
	/// <summary>What a name without an extension may stand for, in the order a shell tries them.</summary>
	private static readonly string[] ProgramExtensions = [".exe", ".com"];

	/// <summary>Files Windows starts through an interpreter rather than as a program of their own.</summary>
	private static readonly string[] ScriptExtensions = [".bat", ".cmd", ".ps1", ".sh", ".vbs", ".js"];

	/// <summary>
	/// The full path of <paramref name="program"/>: a full path that exists, or a bare name found in a PATH folder.
	/// <c>%NAME%</c> is expanded first. A relative path is refused rather than resolved against this app's own working
	/// folder, which is nowhere the user chose.
	/// </summary>
	public static string? Find(string program, LocalMachine machine)
	{
		ArgumentNullException.ThrowIfNull(program);
		ArgumentNullException.ThrowIfNull(machine);
		string expanded = LocalVariables.Expand(program.Trim(), machine.Variable);
		if (expanded.Length == 0)
		{
			return null;
		}

		if (Path.IsPathFullyQualified(expanded))
		{
			return Candidates(expanded).FirstOrDefault(machine.FileExists);
		}

		return expanded.AsSpan().IndexOfAny('\\', '/') >= 0 ? null : FindOnPath(expanded, machine);
	}

	/// <summary>The first PATH folder holding <paramref name="name"/>, with <c>.exe</c> or <c>.com</c> tried when it has neither.</summary>
	public static string? FindOnPath(string name, LocalMachine machine)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(machine);
		string path = machine.Variable("PATH") ?? "";
		foreach (string entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			// A relative entry would be looked up in this app's working folder, which is nowhere the user chose.
			string folder = entry.Trim('"');
			if (!Path.IsPathFullyQualified(folder))
			{
				continue;
			}

			if (Candidates(Path.Combine(folder, name)).FirstOrDefault(machine.FileExists) is { } found)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>True for a script, which a shell runs but Windows cannot start on its own.</summary>
	public static bool IsScript(string program) =>
		ScriptExtensions.Contains(Path.GetExtension(program.Trim().Trim('"')), StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The path itself when it names a program, the program forms of it when it names nothing, and nothing for a
	/// script. CreateProcess wants the exact file and assumes no extension.
	/// </summary>
	private static IEnumerable<string> Candidates(string path)
	{
		string extension = Path.GetExtension(path);
		if (ProgramExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
		{
			return [path];
		}

		return IsScript(path) ? [] : ProgramExtensions.Select(programExtension => path + programExtension);
	}
}
