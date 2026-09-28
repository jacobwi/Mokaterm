using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Common.Components;

/// <summary>The frame every settings page shares, module pages included: page actions and sections.</summary>
public sealed partial class SettingsPage : ComponentBase
{
	private bool _resetting;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	/// <summary>
	/// The page's name in the reset confirmation. The heading itself comes from the page descriptor, which the
	/// settings screen renders above every page, module pages included.
	/// </summary>
	[Parameter, EditorRequired]
	public string Title { get; set; } = "";

	/// <summary>Buttons above the sections, such as an import action.</summary>
	[Parameter]
	public RenderFragment? Actions { get; set; }

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>Runs once the user confirms "Reset to defaults". The button only shows when this is set.</summary>
	[Parameter]
	public EventCallback OnReset { get; set; }

	/// <summary>The reset confirmation text. Defaults to a sentence built from <see cref="Title"/>.</summary>
	[Parameter]
	public string? ResetMessage { get; set; }

	private async Task ConfirmResetAsync()
	{
		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Reset to defaults",
			Message = ResetMessage ?? $"Put every {Title} setting back to its default?",
			ConfirmText = "Reset",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		_resetting = true;
		try
		{
			await OnReset.InvokeAsync();
		}
		finally
		{
			_resetting = false;
		}
	}
}
