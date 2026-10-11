namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// POSIX paths on the docker host as compose writes them. Compose 1.x keeps config files in its labels as they were
/// given (<c>docker-compose.yml</c>, <c>../shared/base.yml</c>), so every path is resolved against the project's folder
/// before it is shown or used.
/// </summary>
internal static class DockerHostPaths
{
	/// <summary>
	/// <paramref name="path"/> as an absolute path: kept when it already is one, otherwise joined to
	/// <paramref name="baseDirectory"/>. <c>.</c> and <c>..</c> are folded either way, the way the shell would, and a
	/// <c>..</c> above the root stays at the root. Null when a relative path has no folder to be relative to.
	/// </summary>
	public static string? Resolve(string? baseDirectory, string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		string trimmed = path.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		string joined;
		if (trimmed.StartsWith('/'))
		{
			joined = trimmed;
		}
		else if (baseDirectory is { Length: > 0 } folder && folder.StartsWith('/'))
		{
			joined = folder.TrimEnd('/') + "/" + trimmed;
		}
		else
		{
			return null;
		}

		List<string> parts = [];
		foreach (string part in joined.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (part == ".")
			{
				continue;
			}

			if (part == "..")
			{
				if (parts.Count > 0)
				{
					parts.RemoveAt(parts.Count - 1);
				}

				continue;
			}

			parts.Add(part);
		}

		return "/" + string.Join('/', parts);
	}

	/// <summary>The folder <paramref name="path"/> is in: <c>/srv/shop</c> for <c>/srv/shop/compose.yaml</c>.</summary>
	public static string DirectoryOf(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		int slash = path.TrimEnd('/').LastIndexOf('/');
		return slash <= 0 ? "/" : path[..slash];
	}

	/// <summary>The last part of <paramref name="path"/>.</summary>
	public static string NameOf(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		string trimmed = path.TrimEnd('/');
		int slash = trimmed.LastIndexOf('/');
		return slash < 0 ? trimmed : trimmed[(slash + 1)..];
	}

	/// <summary>
	/// The project name compose 2.x makes from a folder when the file names none: lower case, and only letters, digits,
	/// <c>-</c> and <c>_</c> kept, starting with a letter or a digit.
	/// </summary>
	public static string ProjectNameFor(string directory)
	{
		ArgumentNullException.ThrowIfNull(directory);
		string name = new([.. NameOf(directory).ToLowerInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')]);
		return name.TrimStart('-', '_');
	}
}
