using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// <see cref="ISessionTabs"/> over the workspace. It only takes window state, so a session tool in its own window
/// borrows the main window's instance and the tab comes up in the window that holds the sessions.
/// </summary>
internal sealed class SessionTabs : ISessionTabs
{
	private readonly SessionActions _actions;
	private readonly IServiceProvider _services;
	private readonly IToolWindows? _windows;

	public SessionTabs(SessionActions actions, IServiceProvider services)
	{
		_actions = actions;
		_services = services;
		_windows = services.GetService<IToolWindows>();
	}

	public void Show(Guid sessionId)
	{
		_actions.Show(sessionId);

		// The request may come from a tool in its own window, which would otherwise stay in front of the new tab.
		_windows?.ShowOwner(_services);
	}
}
