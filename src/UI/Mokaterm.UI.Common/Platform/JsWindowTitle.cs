using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Platform;

internal sealed class JsWindowTitle : IWindowTitle, IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Common/js/interop.js";

	private readonly JsModule _module;

	public JsWindowTitle(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	public async Task ShowAsync(string? subject)
	{
		try
		{
			await _module.InvokeVoidAsync("setWindowTitle", subject);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
		}
	}

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
