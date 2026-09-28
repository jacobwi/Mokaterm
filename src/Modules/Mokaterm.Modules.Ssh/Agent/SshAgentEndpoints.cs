using System.Runtime.InteropServices;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>
/// Finds the agents this machine offers, most standard first: what the settings name, then <c>SSH_AUTH_SOCK</c>, then the
/// Windows OpenSSH pipe, then any Pageant pipe. Pageant's name carries a hash of the user's identity that only its own
/// code can reproduce, so its pipe is found by listing the pipe directory rather than by computing the name.
/// </summary>
internal static class SshAgentEndpoints
{
	public const string OpenSshPipeName = "openssh-ssh-agent";

	public const string AuthSocketVariable = "SSH_AUTH_SOCK";

	private const string PipePrefix = @"\\.\pipe\";

	private const string PageantPipePrefix = "pageant.";

	/// <summary>Every agent that looks reachable right now, in preference order. Empty when none is.</summary>
	public static IReadOnlyList<SshAgentAddress> Discover(string? configured = null)
	{
		List<SshAgentAddress> found = [];
		if (Parse(configured) is { } fromSettings)
		{
			found.Add(fromSettings);
		}

		if (Parse(Environment.GetEnvironmentVariable(AuthSocketVariable)) is { } fromEnvironment)
		{
			Add(found, fromEnvironment);
		}

		if (!OperatingSystem.IsWindows())
		{
			return found;
		}

		string[] pipes = [.. ListPipes()];
		foreach (string pipe in pipes.Where(name => string.Equals(name, OpenSshPipeName, StringComparison.OrdinalIgnoreCase)))
		{
			Add(found, new SshAgentAddress(SshAgentTransport.NamedPipe, pipe));
		}

		foreach (string pipe in pipes.Where(name => name.StartsWith(PageantPipePrefix, StringComparison.OrdinalIgnoreCase)))
		{
			Add(found, new SshAgentAddress(SshAgentTransport.NamedPipe, pipe));
		}

		return found;
	}

	/// <summary>Reads a pipe path, a bare pipe name or a socket path. Null for blank input.</summary>
	public static SshAgentAddress? Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		string trimmed = value.Trim();
		if (trimmed.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith(@"//./pipe/", StringComparison.OrdinalIgnoreCase))
		{
			string name = trimmed[PipePrefix.Length..].Replace('/', '\\');
			return name.Length == 0 ? null : new SshAgentAddress(SshAgentTransport.NamedPipe, name);
		}

		// A bare name is only a pipe on Windows; everywhere else SSH_AUTH_SOCK is a path.
		bool looksLikePath = trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal);
		return OperatingSystem.IsWindows() && !looksLikePath
			? new SshAgentAddress(SshAgentTransport.NamedPipe, trimmed)
			: new SshAgentAddress(SshAgentTransport.UnixSocket, trimmed);
	}

	private static void Add(List<SshAgentAddress> found, SshAgentAddress address)
	{
		if (!found.Contains(address))
		{
			found.Add(address);
		}
	}

	private static IEnumerable<string> ListPipes()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return [];
		}

		try
		{
			// The pipe filesystem has no directories and no metadata; this is the only way to see what is listening.
			return [.. Directory.EnumerateFiles(PipePrefix).Select(Path.GetFileName).OfType<string>()];
		}
		catch (IOException)
		{
			return [];
		}
		catch (UnauthorizedAccessException)
		{
			return [];
		}
	}
}
