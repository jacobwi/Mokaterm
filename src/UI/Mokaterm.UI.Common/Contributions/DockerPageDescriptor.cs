using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Contributions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// A page in the Docker manager's side menu. The manager's own pages register this way too, so a plugin page sits
/// among them by <see cref="Order"/> and is opened by its id like any other.
/// </summary>
public sealed record DockerPageDescriptor : IContributionIdentity
{
	/// <summary>
	/// The stable name the navigation and other pages open it by, such as <c>containers</c>: lower case letters, digits
	/// and dashes. A later registration with the same id replaces an earlier one.
	/// </summary>
	public required string Id { get; init; }

	public required string Title { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	/// <summary>The page. Must derive from <see cref="DockerPageBase"/>.</summary>
	public required Type Component { get; init; }

	/// <summary>Sort key in the navigation; ties fall back to the title. The manager's own pages are 100 apart, Overview at 0.</summary>
	public int Order { get; init; }

	/// <summary>
	/// Why the page cannot be used with this docker host, or null when it can. The page stays in the menu, greyed with
	/// the reason, and still opens. Asked whenever the navigation is drawn, so it reads the context and runs nothing.
	/// </summary>
	public Func<IDockerPageContext, string?>? Unavailable { get; init; }

	string IContributionIdentity.Kind => "Docker page";
	string IContributionIdentity.Id => Id;
	string? IContributionIdentity.Scope => null;
}
