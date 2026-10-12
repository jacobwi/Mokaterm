using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Plugins.Sample;

public sealed partial class SamplePluginTool : SessionViewBase, IAsyncDisposable
{
	private ElementReference _stamp;
	private JsModule? _module;

	[Inject]
	private IJSRuntime JS { get; set; } = default!;

	public async ValueTask DisposeAsync()
	{
		if (_module is not null)
		{
			await _module.DisposeAsync();
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender)
		{
			return;
		}

		// The same path a built-in module uses: the host serves the plugin's wwwroot under its assembly name.
		_module = new JsModule(JS, "./_content/Mokaterm.Plugins.Sample/js/sample.js");
		await _module.InvokeVoidAsync("stamp", _stamp);
	}
}
