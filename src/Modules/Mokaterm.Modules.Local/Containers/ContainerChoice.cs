namespace Mokaterm.Modules.Local.Containers;

/// <summary>One container or pod the options editor offers to pick.</summary>
/// <param name="Name">What the login stores: the container's name or the pod's.</param>
/// <param name="Detail">The image and status of a container, the phase of a pod.</param>
/// <param name="IsRunning">Whether a shell can be opened in it now.</param>
/// <param name="Namespace">The pod's namespace, which picking it also fills in. Null for a container.</param>
/// <param name="Containers">The containers inside a pod, for a pod that has more than one.</param>
public sealed record ContainerChoice(string Name, string Detail, bool IsRunning, string? Namespace, IReadOnlyList<string> Containers);
