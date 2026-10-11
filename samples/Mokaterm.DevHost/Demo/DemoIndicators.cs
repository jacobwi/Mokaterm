using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// The chips an SSH session with agent forwarding shows in the status bar, so they can be looked at without a real
/// server. Nothing is forwarded; the demo has no agent to forward to.
/// </summary>
internal sealed class DemoIndicators : ISessionIndicators
{
	private static readonly SessionIndicator[] Shown =
	[
		new("AGENT", SessionIndicatorState.On, "Agent forwarding to your SSH agent is on. Programs on the server can use it."),
	];

	// Nothing here ever changes, so nobody is told.
	public event Action? Changed
	{
		add { }
		remove { }
	}

	public IReadOnlyList<SessionIndicator> Indicators => Shown;
}
