using Microsoft.JSInterop;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Platform;

/// <summary>Web links through <c>openWebLink</c> in the shared <c>interop.js</c>, the one the terminal's links use too.</summary>
internal sealed class JsUrlOpener : IUrlOpener, IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Common/js/interop.js";

	private readonly JsModule _module;

	public JsUrlOpener(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	public async ValueTask<bool> OpenAsync(string address, CancellationToken cancellationToken = default)
	{
		if (!WebLink.TryCreate(address, out Uri? link))
		{
			return false;
		}

		try
		{
			return await _module.InvokeAsync<bool>("openWebLink", cancellationToken, link.AbsoluteUri);
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			return false;
		}
	}

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
