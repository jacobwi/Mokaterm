namespace Mokaterm.Abstractions.Interaction;

/// <summary>
/// The answer to a <see cref="ConfirmPrompt"/>. <see cref="Remember"/> is only ever true for a prompt that offered
/// <see cref="ConfirmPrompt.RememberText"/>, and it is the caller's job to act on it, because only the caller knows
/// which setting stops the asking.
/// </summary>
public readonly record struct ConfirmResult(bool Confirmed, bool Remember)
{
	/// <summary>A prompt that was cancelled, closed, or never shown.</summary>
	public static ConfirmResult No => new(false, false);
}
