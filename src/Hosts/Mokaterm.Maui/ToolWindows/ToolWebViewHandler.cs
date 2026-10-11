using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// The stock BlazorWebView handler, given a MAUI context whose scope factory is the tool page's. The handler reads its
/// services from <c>MauiContext.Services</c> when it starts the web view, which happens while the view's properties
/// are first mapped, so the context is swapped before that: in <see cref="SetVirtualView"/> when MAUI sets the context
/// first, as it does today, and in <see cref="SetMauiContext"/> should it ever come second.
/// </summary>
internal sealed class ToolWebViewHandler : BlazorWebViewHandler
{
	public override void SetMauiContext(IMauiContext mauiContext)
	{
		ArgumentNullException.ThrowIfNull(mauiContext);
		base.SetMauiContext(VirtualView is ToolWebView view ? Wrap(mauiContext, view) : mauiContext);
	}

	// The IElement overload is sealed in ViewHandler and passes the view on to this one.
	public override void SetVirtualView(IView view)
	{
		if (view is ToolWebView tool && MauiContext is { } context and not ToolMauiContext)
		{
			base.SetMauiContext(Wrap(context, tool));
		}

		base.SetVirtualView(view);
	}

	private static ToolMauiContext Wrap(IMauiContext context, ToolWebView view) =>
		context as ToolMauiContext ?? new ToolMauiContext(context, view.PageScopes);
}
