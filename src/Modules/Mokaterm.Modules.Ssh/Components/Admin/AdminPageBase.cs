using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// One of the system view's own pages: a <see cref="SystemPageBase"/>, which loads, polls and reloads after a change the
/// way every page does, with the module's own scripts, actions and detection beside the public context.
/// </summary>
/// <remarks>
/// Razor components are always public, so every member that names a module type is <c>private protected</c>: the pages
/// see them, nothing outside the module does.
/// </remarks>
public abstract class AdminPageBase : SystemPageBase
{
	/// <summary>
	/// The view's context with the module's types. It is the object a page from another module sees as
	/// <see cref="SystemPageBase.Context"/>, through the public interface.
	/// </summary>
	private protected new AdminPageContext Context => (AdminPageContext)base.Context;

	private protected SystemAdminFeature Feature => Context.Feature;

	private protected SystemProfile Profile => Context.Profile;

	private protected SystemCapabilities Capabilities => Profile.Capabilities;

	/// <summary>Reads <paramref name="script"/> and hands back its output, or throws with what went wrong.</summary>
	private protected async Task<AdminOutcome> ReadAsync(AdminScript script, CancellationToken cancellationToken) =>
		await Feature.ReadAsync(script, cancellationToken);

	/// <summary>
	/// Reviews and runs a change, then reloads the page. <paramref name="input"/> is disposed here whatever happens.
	/// </summary>
	private protected async Task<AdminActionResult?> RunAsync(AdminAction action, AdminInput? input = null)
	{
		using (input)
		{
			AdminActionResult? result = await Context.Run(action, input);
			await ReloadAfterChangeAsync(result is not null);
			return result;
		}
	}
}
