using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Clipboard through <c>clipboard.js</c> over <c>navigator.clipboard</c>, which both the browser and WebView2 provide. One
/// per page; the clear after a secret copy belongs to the window's <see cref="ClipboardClearSchedule"/>, which every page
/// of that window registers with, so closing the page that copied does not leave the secret behind. A write fills the one
/// a click on a <see cref="CopyClick.Class"/> element started, where there is one.
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
		_ = await WriteAsync(text, cancellationToken);

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
		long write = await WriteAsync(secret, cancellationToken);
		_schedule.SecretCopied(this, write);
	}

	// A plain writeText here is refused whenever the window is in the background, and in Safari and Firefox always; the
	// script then waits for the page's next focus, press or key and says so.
	ValueTask<bool> IClipboardClearer.ClearAsync(long copy, long? write, CancellationToken cancellationToken) =>
		_module.InvokeAsync<bool>("clearClipboard", cancellationToken, copy, write);

	ValueTask IClipboardClearer.ForgetClearsBeforeAsync(long copy, CancellationToken cancellationToken) =>
		_module.InvokeVoidAsync("forgetClearsBefore", cancellationToken, copy);

	public void Dispose() => _schedule.Unregister(this);

	public async ValueTask DisposeAsync()
	{
		// Unregistered first, so a clear that comes due now goes to a page that stays.
		_schedule.Unregister(this);
		await _module.DisposeAsync();
	}

	/// <returns>The page's own number for the write, which a later clear hands back.</returns>
	private ValueTask<long> WriteAsync(string text, CancellationToken cancellationToken) =>
		_module.InvokeAsync<long>("writeText", cancellationToken, text);
}
