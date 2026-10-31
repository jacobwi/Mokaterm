using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Contributions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// A page in the System view's side menu. The view's own pages register this way too, so a plugin page sits among them
/// by <see cref="Order"/> and is opened by its id like any other.
/// </summary>
public sealed record SystemPageDescriptor : IContributionIdentity
{
	/// <summary>
	/// The stable name the navigation and other pages open it by, such as <c>services</c> or <c>fail2ban</c>: lower case
	/// letters, digits and dashes. A later registration with the same id replaces an earlier one.
	/// </summary>
	public required string Id { get; init; }

	public required string Title { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	/// <summary>The page. Must derive from <see cref="SystemPageBase"/>.</summary>
	public required Type Component { get; init; }

	/// <summary>Sort key in the navigation; ties fall back to the title. The view's own pages are 100 apart, Overview at 0.</summary>
	public int Order { get; init; }

	/// <summary>
	/// Commands the page or its rule asks about, such as <c>fail2ban-client</c>. Detection looks for them along with its
	/// own, so <see cref="Abstractions.ServerAdmin.ServerFacts.HasCommand"/> answers for them without another round trip.
	/// </summary>
	public IReadOnlyList<string> Commands { get; init; } = [];

	/// <summary>
	/// Why the page cannot be used on this server, or null when it can. The page stays in the menu, greyed with the
	/// reason, and still opens. Asked whenever the navigation is drawn, so it reads the context's facts and never the
	/// server.
	/// </summary>
	public Func<ISystemPageContext, string?>? Unavailable { get; init; }

	string IContributionIdentity.Kind => "System page";
	string IContributionIdentity.Id => Id;
	string? IContributionIdentity.Scope => null;
}
