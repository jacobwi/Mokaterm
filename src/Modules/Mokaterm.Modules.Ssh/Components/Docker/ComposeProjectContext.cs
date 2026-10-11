using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What the project view knows about its project, cascaded to its tabs. Cascaded rather than passed as parameters
/// because the tabs are public components and these types are the module's own.
/// </summary>
/// <param name="Target">How compose commands find the project, or null when there is no compose to run them with.</param>
/// <param name="Services">The services, in the order the files define them.</param>
/// <param name="Version">Bumped by every refresh, so a tab can tell new state from a repeat.</param>
internal sealed record ComposeProjectContext(ComposeProject Project, ComposeTarget? Target, IReadOnlyList<ComposeService> Services, int Version)
{
	/// <summary>The colour class for a container's lines in the merged log: one per service, in the services' order.</summary>
	public string? SourceClass(string container)
	{
		for (int index = 0; index < Services.Count; index++)
		{
			if (Services[index].Containers.Any(item => item.Name == container))
			{
				return $"mt-dk-src-{index % 8}";
			}
		}

		return null;
	}
}
