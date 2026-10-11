namespace Mokaterm.Abstractions.Sessions;

/// <summary>
/// Short facts about a live session that the shell shows beside it, such as agent or X11 forwarding being on. A
/// session offers it through <c>IProtocolSession.GetFeature</c>; the shell draws whatever it lists without knowing
/// which module it came from.
/// </summary>
public interface ISessionIndicators
{
	/// <summary>Raised from any thread when <see cref="Indicators"/> changes.</summary>
	event Action? Changed;

	/// <summary>What to show now, in display order. Empty when there is nothing worth showing.</summary>
	IReadOnlyList<SessionIndicator> Indicators { get; }
}
