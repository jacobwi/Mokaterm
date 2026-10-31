namespace Mokaterm.Abstractions.Contributions;

/// <summary>
/// What a UI contribution is named, so the plugin loader can refuse a plugin that registers one under an id Mokaterm
/// or another plugin already uses, without knowing the contribution kinds. Implemented by the descriptors in
/// Mokaterm.UI.Common.
/// </summary>
public interface IContributionIdentity
{
	/// <summary>
	/// The kind, for the sentence shown when two clash: "app tool", "System page", "Docker page", "session tool",
	/// "settings page" or "protocol".
	/// </summary>
	string Kind { get; }

	/// <summary>The id another contribution of the same <see cref="Kind"/> and <see cref="Scope"/> replaces.</summary>
	string Id { get; }

	/// <summary>The protocol a per-protocol contribution is registered for; null when the kind has no scopes.</summary>
	string? Scope { get; }
}
