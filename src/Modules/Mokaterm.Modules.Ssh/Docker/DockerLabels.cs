namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The labels compose puts on its containers, and a reader for the one form <c>docker ps</c> prints labels in:
/// <c>key=value</c> pairs joined with commas. A value may hold a comma itself (compose joins several config files that
/// way), so a piece without an <c>=</c> belongs to the value before it.
/// </summary>
internal static class DockerLabels
{
	public const string ComposeProject = "com.docker.compose.project";

	public const string ComposeService = "com.docker.compose.service";

	public const string ComposeWorkingDirectory = "com.docker.compose.project.working_dir";

	public const string ComposeConfigFiles = "com.docker.compose.project.config_files";

	/// <summary>Set on containers <c>compose run</c> starts, which are not part of the project's services.</summary>
	public const string ComposeOneOff = "com.docker.compose.oneoff";

	/// <summary>
	/// The env files a project was started with through <c>--env-file</c>, comma-joined. Recent composes set it; older
	/// ones leave no trace of the option.
	/// </summary>
	public const string ComposeEnvironmentFile = "com.docker.compose.project.environment_file";

	// The hash compose worked out from the service's definition when it made the container: up compares it with the
	// files' hash to decide whether to make the container again.
	public const string ComposeConfigHash = "com.docker.compose.config-hash";

	// The compose that made the container. 1.x and 2.x work the hash out differently, so their hashes never compare.
	public const string ComposeVersion = "com.docker.compose.version";

	public static IReadOnlyDictionary<string, string> Parse(string? text)
	{
		Dictionary<string, string> labels = new(StringComparer.Ordinal);
		string? lastKey = null;
		foreach (string piece in (text ?? "").Split(','))
		{
			int equals = piece.IndexOf('=', StringComparison.Ordinal);
			if (equals > 0 && IsKey(piece.AsSpan(0, equals)))
			{
				lastKey = piece[..equals].Trim();
				labels[lastKey] = piece[(equals + 1)..];
			}
			else if (lastKey is not null)
			{
				labels[lastKey] = labels[lastKey] + "," + piece;
			}
		}

		return labels;
	}

	/// <summary>The paths of a comma-joined list such as the config files label, without empty entries.</summary>
	public static IReadOnlyList<string> SplitList(string? text) =>
		(text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	/// <summary>Label keys are reverse DNS names in practice; anything with a space or a slash is part of a value.</summary>
	private static bool IsKey(ReadOnlySpan<char> key)
	{
		ReadOnlySpan<char> trimmed = key.Trim();
		if (trimmed.IsEmpty)
		{
			return false;
		}

		foreach (char character in trimmed)
		{
			if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
			{
				return false;
			}
		}

		return true;
	}
}
