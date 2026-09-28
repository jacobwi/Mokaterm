using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.Interaction;

/// <summary>
/// Base for the queued prompt dialogs. After the first render it focuses the <c>data-autofocus</c> element and lets
/// Enter press the <c>data-primary</c> button. Each prompt answers exactly once.
/// </summary>
public abstract class PromptDialogBase : ComponentBase
{
	private bool _answered;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	/// <summary>The dialog body; assign it with <c>@ref</c>.</summary>
	protected PromptBody? Body { get; set; }

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender && Body is not null)
		{
			await Forms.PrepareAsync(Body.Element, dialog: true, submitOnEnter: true);
		}
	}

	/// <summary>Invokes <paramref name="callback"/> with <paramref name="value"/> unless the prompt was already answered.</summary>
	protected Task AnswerAsync<TValue>(EventCallback<TValue> callback, TValue value)
	{
		if (_answered)
		{
			return Task.CompletedTask;
		}

		_answered = true;
		return callback.InvokeAsync(value);
	}

	/// <summary>A <c>data-*</c> attribute value that is present only when <paramref name="condition"/> holds.</summary>
	protected static string? Flag(bool condition) => condition ? "true" : null;
}
