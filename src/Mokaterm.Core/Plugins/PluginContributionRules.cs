using Mokaterm.Abstractions.Contributions;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// The contribution ids a plugin may register under: none that Mokaterm or an earlier plugin already uses for the same
/// kind and scope. Without this a plugin's tool, page or protocol with a built-in's id would silently replace it,
/// because the last registration with an id wins.
/// </summary>
public static class PluginContributionRules
{
	/// <summary>
	/// Why the contributions a plugin registered cannot be kept, or null. <paramref name="inUse"/> holds the ids
	/// registered before it, built-in and plugin alike.
	/// </summary>
	public static string? Problem(IEnumerable<IContributionIdentity> contributions, IReadOnlySet<ContributionKey> inUse)
	{
		ArgumentNullException.ThrowIfNull(contributions);
		ArgumentNullException.ThrowIfNull(inUse);
		foreach (IContributionIdentity contribution in contributions)
		{
			if (!inUse.Contains(new ContributionKey(contribution.Kind, contribution.Scope, contribution.Id)))
			{
				continue;
			}

			return contribution.Scope is { } scope
				? $"Its {contribution.Kind} \"{contribution.Id}\" for protocol \"{scope}\" is already used by Mokaterm or another plugin, so it was not added."
				: $"Its {contribution.Kind} id \"{contribution.Id}\" is already used by Mokaterm or another plugin, so it was not added.";
		}

		return null;
	}

	/// <summary>One contribution: its kind, the protocol it is scoped to when it has one, and its id.</summary>
	public readonly record struct ContributionKey(string Kind, string? Scope, string Id);
}
