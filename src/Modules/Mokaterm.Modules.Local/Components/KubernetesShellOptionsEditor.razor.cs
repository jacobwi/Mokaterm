using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Containers;
using Mokaterm.Modules.Local.Shells;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Local.Components;

/// <summary>
/// The Kubernetes pod part of the connection editor: which cluster and namespace, which pod and container, and what
/// runs there. Each field says what is wrong with it as it is typed, in the words the connect would fail with.
/// </summary>
public partial class KubernetesShellOptionsEditor : ConnectionOptionsEditorBase<KubernetesShellOptions>
{
	/// <summary>The containers of the pod picked last, so the container field can name them.</summary>
	private IReadOnlyList<string> _podContainers = [];

	private string? PodError => Problem(Current.Pod, ContainerNames.PodProblem);

	private string? ContextError => Problem(Current.Context, ContainerNames.KubeContextProblem);

	private string? NamespaceError => Problem(Current.Namespace, ContainerNames.NamespaceProblem);

	private string? ContainerError => Problem(Current.Container, ContainerNames.PodContainerProblem);

	private string ContainerHelp => _podContainers.Count > 1
		? $"This pod has {string.Join(", ", _podContainers)}."
		: "For a pod with more than one container. Empty takes the pod's default.";

	protected override KubernetesShellOptions From(ProtocolOptions options) => KubernetesShellOptions.From(options);

	protected override ProtocolOptions ApplyTo(KubernetesShellOptions current, ProtocolOptions options) => current.ApplyTo(options);

	/// <summary>An empty field is not a problem: it leaves the choice to kubectl or to the host's address.</summary>
	private static string? Problem(string? value, Func<string?, string?> rule) =>
		ContainerNames.Value(value) is { } trimmed ? rule(trimmed) : null;

	/// <summary>Lists against what the fields say now, so a namespace typed a moment ago is the one asked.</summary>
	private Task<ContainerChoiceList> ListAsync(CancellationToken cancellationToken) =>
		ContainerListing.KubernetesAsync(Current, LocalMachine.Current(), LocalCommandRunner.Instance, cancellationToken);

	/// <summary>
	/// Takes the pod and its namespace, and lets go of a container the new pod does not have: kubectl would refuse it.
	/// </summary>
	private Task OnPickAsync(ContainerChoice choice)
	{
		_podContainers = choice.Containers;
		string? container = ContainerNames.Value(Current.Container);
		bool keepContainer = container is not null && choice.Containers.Contains(container, StringComparer.Ordinal);
		return ChangeAsync(Current with
		{
			Pod = choice.Name,
			Namespace = choice.Namespace ?? Current.Namespace,
			Container = keepContainer ? Current.Container : null,
		});
	}

	private Task OnPodChangedAsync(string? value)
	{
		_podContainers = [];
		return ChangeAsync(Current with { Pod = value });
	}

	private Task OnContextChangedAsync(string? value) => ChangeAsync(Current with { Context = value });

	private Task OnNamespaceChangedAsync(string? value) => ChangeAsync(Current with { Namespace = value });

	private Task OnContainerChangedAsync(string? value) => ChangeAsync(Current with { Container = value });

	private Task OnShellChangedAsync(ContainerShell value) => ChangeAsync(Current with { Shell = value });

	private Task OnCommandChangedAsync(string? value) => ChangeAsync(Current with { Command = value });
}
