using System.Text.RegularExpressions;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>
/// The rules for every value a container or pod login puts on a docker or kubectl command line. Each method returns
/// why the value cannot be used, or null when it can, so the editor shows the same sentence the connect would fail with.
/// </summary>
/// <remarks>
/// Every value is also quoted on the way out and passed in <c>--name=value</c> form, so none of these exists to keep
/// the command line intact. They refuse what the tool would refuse anyway, earlier and in a sentence: a pod name with
/// capitals, a context with a slash, a user with a space.
/// </remarks>
internal static partial class ContainerNames
{
	/// <summary>Kubernetes caps a DNS subdomain name (a pod) at 253 characters.</summary>
	private const int MaxSubdomainLength = 253;

	/// <summary>Kubernetes caps a DNS label (a namespace, a container) at 63 characters.</summary>
	private const int MaxLabelLength = 63;

	private const int MaxValueLength = 1024;

	/// <summary>The value trimmed, or null when nothing is left.</summary>
	public static string? Value(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	/// <summary>A docker context's name, by the rule <c>docker context create</c> applies.</summary>
	public static string? DockerContextProblem(string? context) =>
		Basic(context, "Docker context") ?? (DockerContext().IsMatch(context!)
			? null
			: "A Docker context is named with letters, digits, '_', '.', '+' and '-', starting with a letter or a digit.");

	/// <summary>A daemon address for <c>--host</c>: one of the schemes the docker CLI dials.</summary>
	public static string? DockerHostProblem(string? host) =>
		Basic(host, "Docker host") ?? (DockerHost().IsMatch(host!)
			? null
			: "A Docker host is an address such as ssh://user@server, tcp://server:2376, npipe:////./pipe/docker_engine or unix:///var/run/docker.sock.");

	/// <summary>A user for <c>--user</c>: a name or a uid, with a group or gid after a colon.</summary>
	public static string? UserProblem(string? user) =>
		Basic(user, "user") ?? (ContainerUser().IsMatch(user!)
			? null
			: "A user is a name or a uid, with :group or :gid after it if needed.");

	/// <summary>A folder inside a container: absolute, as a Linux or a Windows container writes one.</summary>
	public static string? FolderProblem(string? folder) =>
		Basic(folder, "working folder") ?? (folder!.StartsWith('/') || WindowsFolder().IsMatch(folder)
			? null
			: $"The working folder {folder} is not an absolute path inside the container.");

	/// <summary>A pod's name: a DNS subdomain, which is what Kubernetes names pods by.</summary>
	public static string? PodProblem(string? pod) =>
		Basic(pod, "pod") ?? (pod!.Length <= MaxSubdomainLength && Subdomain().IsMatch(pod)
			? null
			: "A pod is named with lower-case letters, digits, '-' and '.', starting and ending with a letter or a digit.");

	public static string? NamespaceProblem(string? name) => LabelProblem(name, "namespace");

	public static string? PodContainerProblem(string? name) => LabelProblem(name, "container");

	/// <summary>
	/// A kubeconfig context. Kubernetes puts no rule on these (cloud tools write ARNs and slashes into them), so only what
	/// a command line should never carry is refused.
	/// </summary>
	public static string? KubeContextProblem(string? context) => Basic(context, "Kubernetes context");

	private static string? LabelProblem(string? name, string kind) =>
		Basic(name, kind) ?? (name!.Length <= MaxLabelLength && Label().IsMatch(name)
			? null
			: $"A {kind} is named with lower-case letters, digits and '-', starting and ending with a letter or a digit, {MaxLabelLength} at most.");

	private static string? Basic(string? value, string kind)
	{
		if (string.IsNullOrEmpty(value))
		{
			return $"The {kind} is missing.";
		}

		if (value.Length > MaxValueLength)
		{
			return $"The {kind} is longer than {MaxValueLength} characters.";
		}

		return value.Any(char.IsControl) ? $"The {kind} holds a control character." : null;
	}

	[GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.+-]+\z", RegexOptions.CultureInvariant)]
	private static partial Regex DockerContext();

	[GeneratedRegex(@"^(tcp|ssh|npipe|unix)://[^\s]+\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex DockerHost();

	[GeneratedRegex(@"^[a-zA-Z0-9_][a-zA-Z0-9_.@$-]{0,63}(:[a-zA-Z0-9_][a-zA-Z0-9_.@$-]{0,63})?\z", RegexOptions.CultureInvariant)]
	private static partial Regex ContainerUser();

	[GeneratedRegex(@"^[a-zA-Z]:[\\/]", RegexOptions.CultureInvariant)]
	private static partial Regex WindowsFolder();

	[GeneratedRegex(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?(\.[a-z0-9]([-a-z0-9]*[a-z0-9])?)*\z", RegexOptions.CultureInvariant)]
	private static partial Regex Subdomain();

	[GeneratedRegex(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?\z", RegexOptions.CultureInvariant)]
	private static partial Regex Label();
}
