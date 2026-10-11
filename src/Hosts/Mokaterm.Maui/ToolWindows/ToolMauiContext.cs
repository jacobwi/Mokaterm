using Microsoft.Extensions.DependencyInjection;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// The window's MAUI context with one change: asked for a scope factory, it gives the tool page's. Blazor's
/// WebViewManager creates its page scope through exactly that lookup, and everything else the handler asks for (its
/// dispatcher, logger, developer tools) stays the window's.
/// </summary>
internal sealed class ToolMauiContext : IMauiContext
{
	public ToolMauiContext(IMauiContext window, IServiceScopeFactory pageScopes)
	{
		Window = window;
		Services = new ScopeSwitchingProvider(window.Services, pageScopes);
	}

	/// <summary>The context MAUI gave the handler.</summary>
	public IMauiContext Window { get; }

	public IServiceProvider Services { get; }

	public IMauiHandlersFactory Handlers => Window.Handlers;

	private sealed class ScopeSwitchingProvider(IServiceProvider window, IServiceScopeFactory pageScopes) : IServiceProvider
	{
		public object? GetService(Type serviceType) =>
			serviceType == typeof(IServiceScopeFactory) ? pageScopes : window.GetService(serviceType);
	}
}
