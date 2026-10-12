using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Maui.Plugins;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// The BlazorWebView of a tool window. It carries the scope factory its page is created from, which
/// <see cref="ToolWebViewHandler"/> hands to Blazor in place of the window's own.
/// </summary>
internal sealed class ToolWebView : MokatermWebView
{
	public ToolWebView(IServiceScopeFactory pageScopes)
	{
		ArgumentNullException.ThrowIfNull(pageScopes);
		PageScopes = pageScopes;
	}

	/// <summary>Makes the page's scope from the borrowed services (<see cref="ToolPageServices.Borrow"/>).</summary>
	public IServiceScopeFactory PageScopes { get; }
}
