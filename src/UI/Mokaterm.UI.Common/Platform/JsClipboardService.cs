using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Clipboard through <c>navigator.clipboard</c>, which both the browser and WebView2 provide. One per page; the clear
/// after a secret copy belongs to the window's <see cref="ClipboardClearSchedule"/>, which every page of that window
/// registers with, so closing the page that copied does not leave the secret behind.
/// </summary>
internal sealed class JsClipboardService : IClipboardService, IClipboardClearer, IDisposable, IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Common/js/clipboard.js";

	private readonly IJSRuntime _jsRuntime;
	private readonly JsModule _module;
	private readonly ClipboardClearSchedule _schedule;

	public JsClipboardService(IJSRuntime jsRuntime, ClipboardClearSchedule schedule)
	{
		_jsRuntime = jsRuntime;
		_module = new JsModule(jsRuntime, ModulePath);
		_schedule = schedule;
		_schedule.Register(this);
	}

	public async ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
	{
		await WriteAsync(text, cancellationToken);

		// Only after the write: one the page refused leaves the secret there, and its clear has to stay.
		_schedule.TextCopied();
	}

	public async ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			return await _jsRuntime.InvokeAsync<string?>("navigator.clipboard.readText", cancellationToken);
		}
		catch (JSException)
		{
			// Permission denied or no text on the clipboard.
			return null;
		}
	}

	public async ValueTask WriteSecretAsync(string secret, CancellationToken cancellationToken = default)
	{
		await WriteAsync(secret, cancellationToken);
		_schedule.SecretCopied(this);
	}

	// A plain writeText here is refused whenever the window is in the background; the script waits for focus.
	ValueTask IClipboardClearer.ClearAsync(long copy, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("clearClipboard", cancellationToken, copy);

	ValueTask IClipboardClearer.ForgetClearsBeforeAsync(long copy, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("forgetClearsBefore", cancellationToken, copy);

	public void Dispose() => _schedule.Unregister(this);

	public async ValueTask DisposeAsync()
	{
		// Unregistered first, so a clear that comes due now goes to a page that stays.
		_schedule.Unregister(this);
		await _module.DisposeAsync();
	}

	private ValueTask WriteAsync(string text, CancellationToken cancellationToken) =>
		_jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
}
