using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Mokaterm.UI.Common.Interop;

/// <summary>
/// Starting focus and Enter handling for forms and dialogs. Focus goes to the element marked <c>data-autofocus</c> and
/// stays there until the user types or clicks, because a closing menu or palette puts focus back where it was and that
/// can land later. One import per scope.
/// </summary>
public sealed class FormInterop : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Common/js/forms.js";

	private readonly JsModule _module;

	public FormInterop(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	/// <summary>
	/// Focuses the <c>data-autofocus</c> element inside the form (or the dialog around <paramref name="anchor"/>) and,
	/// with <paramref name="submitOnEnter"/>, lets Enter click the <c>data-primary</c> button.
	/// </summary>
	public ValueTask<bool> PrepareAsync(ElementReference anchor, bool dialog, bool submitOnEnter) =>
		_module.TryInvokeVoidAsync("prepareForm", anchor, new { dialog, submitOnEnter });

	/// <summary>Moves focus to the <c>data-autofocus</c> element inside <paramref name="root"/>.</summary>
	public ValueTask<bool> FocusPreferredAsync(ElementReference root, bool selectText = false) =>
		_module.TryInvokeVoidAsync("focusPreferred", root, selectText);

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
