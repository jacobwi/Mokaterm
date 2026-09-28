using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Common.Interaction;

/// <summary>
/// The prompts a user meets often enough to want silenced: closing a connected session, pasting several lines. Each
/// offers <see cref="Label"/> and turns off the setting behind it, which is also the setting that turns it back on.
/// </summary>
public static class PromptOptOut
{
	/// <summary>The check box label, the same wherever a prompt can be turned off.</summary>
	public const string Label = "Don't ask again";

	/// <summary>
	/// Turns the prompt off and says which settings page brings it back. A write that fails only loses the preference:
	/// whatever the user confirmed has already been decided, so the failure is logged and nothing else happens.
	/// </summary>
	/// <param name="notice">What stops happening and where to turn it back on, in one sentence.</param>
	public static async Task ApplyAsync<TSection>(
		ISettingsService settings,
		IUserInteraction interaction,
		ILogger logger,
		Func<TSection, TSection> turnOff,
		string notice)
		where TSection : class, ISettingsSection, new()
	{
		try
		{
			await settings.UpdateAsync(turnOff);
			interaction.Notify(NoticeSeverity.Info, notice);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Turning the {Section} prompt off failed.", TSection.SectionKey);
		}
	}
}
