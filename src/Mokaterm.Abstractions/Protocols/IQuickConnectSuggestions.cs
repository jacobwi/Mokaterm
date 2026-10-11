namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// Optional on an <see cref="IProtocolProvider"/> whose addresses name something on this computer, such as a serial
/// port or a local shell. The quick connect box asks for the names while it is open and checks a typed one before it
/// connects, so the shell offers them without knowing any module.
/// </summary>
public interface IQuickConnectSuggestions
{
	/// <summary>
	/// The addresses this protocol could open right now. The box asks every time it opens, so this reads what the
	/// machine already lists (the registry, a folder, an API call) and never runs a program or touches the network.
	/// </summary>
	IReadOnlyList<QuickConnectSuggestion> GetQuickConnectSuggestions();

	/// <summary>Why <paramref name="address"/> cannot be opened, in one sentence, or null when it can.</summary>
	string? ValidateQuickConnectAddress(string address) => null;
}
