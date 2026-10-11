using System.Text;

namespace Mokaterm.Generators;

/// <summary>
/// The full name the Razor compiler gives a component, worked out the way it does: the file's own <c>@namespace</c>;
/// otherwise the nearest <c>_Imports.razor</c> above it that sets one, plus the folders in between; otherwise the root
/// namespace plus the folders under the project. A wrong answer here is a compile error in the generated file, never a
/// row filed under the wrong page.
/// </summary>
internal static class RazorTypeNames
{
	public static string? Of(RazorFile page, IReadOnlyList<RazorFile> imports, ProjectInfo project)
	{
		string projectDirectory = Normalize(project.ProjectDirectory).TrimEnd('/') + "/";
		string path = Normalize(page.Path);
		if (!path.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string directory = DirectoryOf(path);
		string className = Identifier(Path.GetFileNameWithoutExtension(page.Path));
		if (page.Namespace is { } own)
		{
			return own + "." + className;
		}

		RazorFile? nearest = null;
		string nearestDirectory = "";
		foreach (RazorFile import in imports)
		{
			string importDirectory = DirectoryOf(Normalize(import.Path));
			if (import.Namespace is not null
				&& directory.StartsWith(importDirectory, StringComparison.OrdinalIgnoreCase)
				&& importDirectory.Length > nearestDirectory.Length)
			{
				nearest = import;
				nearestDirectory = importDirectory;
			}
		}

		return nearest is not null
			? Append(nearest.Namespace!, directory.Substring(nearestDirectory.Length)) + "." + className
			: Append(project.RootNamespace, directory.Substring(projectDirectory.Length)) + "." + className;
	}

	private static string Append(string @namespace, string relativeDirectory)
	{
		StringBuilder name = new(@namespace);
		foreach (string segment in relativeDirectory.Split(['/'], StringSplitOptions.RemoveEmptyEntries))
		{
			name.Append('.').Append(Identifier(segment));
		}

		return name.ToString();
	}

	/// <summary>Razor's own rule for a folder or file name: anything a C# identifier cannot hold becomes an underscore.</summary>
	private static string Identifier(string name)
	{
		StringBuilder identifier = new(name.Length + 1);
		if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
		{
			identifier.Append('_');
		}

		foreach (char character in name)
		{
			identifier.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
		}

		return identifier.ToString();
	}

	private static string DirectoryOf(string path) => path.Substring(0, path.LastIndexOf('/') + 1);

	private static string Normalize(string path) => path.Replace('\\', '/');
}
