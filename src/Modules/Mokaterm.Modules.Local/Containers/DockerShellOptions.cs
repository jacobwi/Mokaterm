using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>Typed access to the <c>docker.*</c> keys a Docker container login keeps in its <see cref="ProtocolOptions"/>.</summary>
/// <remarks>
/// Every value is an override, so a login carries only what makes it different: no container opens the one the host's
/// address names, and no context uses docker's current one. Values are kept as typed, because the editor reports every
/// keystroke; whoever reads a value trims it.
/// </remarks>
public sealed record DockerShellOptions
{
	/// <summary>Every key this protocol owns starts with this.</summary>
	public const string KeyPrefix = "docker.";

	public const string ContainerKey = "docker.container";

	public const string ContextKey = "docker.context";

	public const string HostKey = "docker.host";

	public const string UserKey = "docker.user";

	public const string FolderKey = "docker.folder";

	public const string ShellKey = "docker.shell";

	public const string CommandKey = "docker.command";

	public static DockerShellOptions Default { get; } = new();

	/// <summary>
	/// The container to open, which wins over the host's address the way <c>serial.port</c> wins over a serial login's:
	/// the editor can fill this in from a list, and it cannot change the address.
	/// </summary>
	public string? Container { get; init; }

	/// <summary>A docker context (<c>docker context ls</c>). Null uses the current one.</summary>
	public string? Context { get; init; }

	/// <summary>A daemon address such as <c>ssh://user@server</c>, instead of a context. Docker refuses the two together.</summary>
	public string? Host { get; init; }

	/// <summary>The user the shell runs as (<c>--user</c>): a name or uid, with <c>:group</c> if needed. Null keeps the image's.</summary>
	public string? User { get; init; }

	/// <summary>The folder the shell starts in (<c>--workdir</c>). Null keeps the image's.</summary>
	public string? Folder { get; init; }

	public ContainerShell Shell { get; init; } = ContainerShell.Auto;

	/// <summary>What a <see cref="ContainerShell.Custom"/> login runs in the container, passed on as typed.</summary>
	public string? Command { get; init; }

	public static DockerShellOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new DockerShellOptions
		{
			Container = Kept(options.GetString(ContainerKey)),
			Context = Kept(options.GetString(ContextKey)),
			Host = Kept(options.GetString(HostKey)),
			User = Kept(options.GetString(UserKey)),
			Folder = Kept(options.GetString(FolderKey)),
			Shell = options.GetEnum(ShellKey, ContainerShell.Auto),
			Command = Kept(options.GetString(CommandKey)),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options
			.With(ContainerKey, Kept(Container))
			.With(ContextKey, Kept(Context))
			.With(HostKey, Kept(Host))
			.With(UserKey, Kept(User))
			.With(FolderKey, Kept(Folder))
			.WithEnum<ContainerShell>(ShellKey, Shell == ContainerShell.Auto ? null : Shell)
			.With(CommandKey, Kept(Command));
	}

	private static string? Kept(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// A command line is where a token ends up, and a daemon address can carry a user, so both only say they are set.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"Container = {Container}, Context = {Context}, Host = {SecretText.Describe(Host)}, User = {User}, Folder = {Folder}, Shell = {Shell}, Command = {SecretText.Describe(Command)}");
		return true;
	}
}
