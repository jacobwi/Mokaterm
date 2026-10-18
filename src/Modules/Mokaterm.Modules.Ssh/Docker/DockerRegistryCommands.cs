using System.Globalization;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The registry commands: reading which registries the client is logged in to, <c>docker login</c> with the password on
/// stdin, and <c>docker logout</c>. They run through the manager's shell like every other call, so through sudo they
/// read and change root's client config, which is the one root's docker uses.
/// </summary>
internal static class DockerRegistryCommands
{
	/// <summary>The longest password or token passed on: a service account's JSON key runs to a few kilobytes.</summary>
	public const int MaxPasswordBytes = 64 * 1024;

	/// <summary>
	/// Prints the account (<c>id -un</c>), the file, then <see cref="DockerClientConfigReader.Marker"/> and the file when it
	/// exists. sed blanks every credential value on the server where it can (a value that is there becomes <c>"1"</c>,
	/// an empty one stays empty), so the values only cross the connection from a file whose layout sed cannot follow,
	/// and the reader drops them there. <c>$HOME</c> is whatever this shell has, which under sudo is what root's docker
	/// uses too.
	/// </summary>
	private static readonly string ClientConfigScript = $$"""
		f="${DOCKER_CONFIG:-$HOME/.docker}/config.json"
		printf '%s\n%s\n' "$(id -un 2>/dev/null)" "$f"
		[ -e "$f" ] || exit 0
		[ -r "$f" ] || { echo "$f cannot be read by this account." >&2; exit 2; }
		s=$(wc -c < "$f" | tr -d ' ')
		[ "$s" -le {{DockerClientConfigReader.MaxFileBytes.ToString(CultureInfo.InvariantCulture)}} ] 2>/dev/null || { echo "$f is larger than 1 MB, so it is not read." >&2; exit 3; }
		printf '{{DockerClientConfigReader.Marker}}\n'
		if printf 'a' | sed -E 's/a/b/' >/dev/null 2>&1; then
			exec sed -E 's/"(auth|password|identitytoken|registrytoken)"([[:space:]]*):([[:space:]]*)"([^"\\]|\\.)+"/"\1"\2:\3"1"/g' < "$f"
		fi
		exec cat < "$f"
		""".ReplaceLineEndings("\n");

	/// <summary>Reads the client config through <see cref="DockerClientConfigReader"/>; stdout stays bytes so it can be wiped.</summary>
	public static DockerCall ReadClientConfig() => DockerCall.Script(ClientConfigScript, []) with
	{
		BinaryOutput = true,
		OutputLimitBytes = DockerClientConfigReader.MaxFileBytes + 8192,
		Timeout = TimeSpan.FromSeconds(20),
	};

	/// <summary>
	/// <c>docker login --username=USER --password-stdin [REGISTRY]</c> with <paramref name="password"/> as its stdin: the
	/// password is never a word of the line, so nothing that shows the call shows it. No registry logs in to Docker Hub.
	/// </summary>
	/// <param name="password">Kept by the caller, who disposes it once the call ran.</param>
	/// <exception cref="ArgumentException">The registry, the user or the password breaks its rule.</exception>
	public static DockerCall Login(string? registry, string user, SecretBuffer password)
	{
		ArgumentNullException.ThrowIfNull(password);
		if (PasswordProblem(password) is { } problem)
		{
			throw new ArgumentException(problem, nameof(password));
		}

		string checkedUser = DockerCommands.Checked(user, DockerNames.RegistryUserProblem(user));
		List<string> words = ["login", "--username=" + checkedUser, "--password-stdin"];
		if (DockerRegistryNames.ForLogin(registry) is { } server)
		{
			words.Add(server);
		}

		return new DockerCall { Program = DockerProgram.Docker, Arguments = words, Input = password, Timeout = TimeSpan.FromMinutes(1) }.AsChange(TimeSpan.FromMinutes(1));
	}

	/// <summary><c>docker logout KEY</c>, with the key as the client config has it.</summary>
	public static DockerCall Logout(string key)
	{
		ArgumentNullException.ThrowIfNull(key);
		string server = DockerCommands.Checked(key.Trim(), DockerNames.RegistryProblem(key));
		return new DockerCall { Program = DockerProgram.Docker, Arguments = ["logout", server] }.AsChange(TimeSpan.FromSeconds(30));
	}

	/// <summary>Why a password cannot be sent, or null.</summary>
	public static string? PasswordProblem(SecretBuffer password)
	{
		ArgumentNullException.ThrowIfNull(password);
		return password.Length switch
		{
			0 => "Type the password or token.",
			> MaxPasswordBytes => "The password is longer than 64 KB.",
			_ => null,
		};
	}

	/// <summary>
	/// What docker printed on stderr about where it keeps the password, as it said it: the warning that it went into
	/// <c>config.json</c> unencrypted. Empty when it said nothing of the kind.
	/// </summary>
	public static string StorageWarning(DockerOutput output)
	{
		ArgumentNullException.ThrowIfNull(output);
		string error = Mokaterm.Abstractions.Terminal.TerminalTextFilter.Plain(output.Error).Trim();
		return error.Contains("unencrypted", StringComparison.OrdinalIgnoreCase) ? error : "";
	}
}
