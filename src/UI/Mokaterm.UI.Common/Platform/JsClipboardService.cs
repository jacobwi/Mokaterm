using Microsoft.JSInterop;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Platform;

/// <summary>Clipboard through <c>navigator.clipboard</c>, which both the browser and WebView2 provide.</summary>
internal sealed class JsClipboardService : IClipboardService, IDisposable, IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Common/js/clipboard.js";

	private readonly IJSRuntime _jsRuntime;
	private readonly JsModule _module;
	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private ITimer? _clearTimer;

	public JsClipboardService(IJSRuntime jsRuntime, ISettingsService settings, TimeProvider timeProvider)
	{
		_jsRuntime = jsRuntime;
		_module = new JsModule(jsRuntime, ModulePath);
		_settings = settings;
		_timeProvider = timeProvider;
	}

	public async ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
	{
		CancelPendingClear();
		await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
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
		await WriteTextAsync(secret, cancellationToken);

		int seconds = _settings.Get<SecuritySettings>().ClipboardClearSeconds;
		if (seconds > 0)
		{
			ITimer timer = _timeProvider.CreateTimer(_ => _ = ClearAsync(), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
			Interlocked.Exchange(ref _clearTimer, timer)?.Dispose();
		}
	}

	public void Dispose() => CancelPendingClear();

	public async ValueTask DisposeAsync()
	{
		CancelPendingClear();
		await _module.DisposeAsync();
	}

	private async Task ClearAsync()
	{
		try
		{
			// A plain writeText here is refused whenever the window is in the background; the script waits for focus.
			await _module.InvokeVoidAsync("clearClipboard");
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Best effort: the window may be gone.
		}
	}

	private void CancelPendingClear() => Interlocked.Exchange(ref _clearTimer, null)?.Dispose();
}
