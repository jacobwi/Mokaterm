using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>Typed access to the <c>kubernetes.*</c> keys a Kubernetes pod login keeps in its <see cref="ProtocolOptions"/>.</summary>
/// <remarks>
/// Every value is an override: no pod opens the one the host's address names, and no context, namespace or container
/// takes what kubectl would pick. Values are kept as typed, because the editor reports every keystroke; whoever reads a
/// value trims it.
/// </remarks>
public sealed record KubernetesShellOptions
{
	/// <summary>Every key this protocol owns starts with this.</summary>
	public const string KeyPrefix = "kubernetes.";

	public const string PodKey = "kubernetes.pod";

	public const string ContextKey = "kubernetes.context";

	public const string NamespaceKey = "kubernetes.namespace";

	public const string ContainerKey = "kubernetes.container";

	public const string ShellKey = "kubernetes.shell";

	public const string CommandKey = "kubernetes.command";

	public static KubernetesShellOptions Default { get; } = new();

	/// <summary>The pod to open, which wins over the host's address: the editor fills it in from a list.</summary>
	public string? Pod { get; init; }

	/// <summary>A kubeconfig context (<c>kubectl config get-contexts</c>). Null uses the current one.</summary>
	public string? Context { get; init; }

	/// <summary>The pod's namespace. Null uses the context's own.</summary>
	public string? Namespace { get; init; }

	/// <summary>The container inside the pod. Null lets kubectl pick the pod's default one.</summary>
	public string? Container { get; init; }

	public ContainerShell Shell { get; init; } = ContainerShell.Auto;

	/// <summary>What a <see cref="ContainerShell.Custom"/> login runs in the container, passed on as typed.</summary>
	public string? Command { get; init; }

	public static KubernetesShellOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new KubernetesShellOptions
		{
			Pod = Kept(options.GetString(PodKey)),
			Context = Kept(options.GetString(ContextKey)),
			Namespace = Kept(options.GetString(NamespaceKey)),
			Container = Kept(options.GetString(ContainerKey)),
			Shell = options.GetEnum(ShellKey, ContainerShell.Auto),
			Command = Kept(options.GetString(CommandKey)),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options
			.With(PodKey, Kept(Pod))
			.With(ContextKey, Kept(Context))
			.With(NamespaceKey, Kept(Namespace))
			.With(ContainerKey, Kept(Container))
			.WithEnum<ContainerShell>(ShellKey, Shell == ContainerShell.Auto ? null : Shell)
			.With(CommandKey, Kept(Command));
	}

	private static string? Kept(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// A command line is where a token ends up, so it only says whether it is set.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"Pod = {Pod}, Context = {Context}, Namespace = {Namespace}, Container = {Container}, Shell = {Shell}, Command = {SecretText.Describe(Command)}");
		return true;
	}
}
