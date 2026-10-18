using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Common.Extensions;

public static class UiCommonServiceCollectionExtensions
{
	/// <summary>Registers the shared UI services. Called by the shell's <c>AddMokatermUI()</c>; safe to call twice.</summary>
	public static IServiceCollection AddMokatermUiCommon(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		// Window state, not a page service: a tool window borrows its opener's, so a secret copied there is cleared
		// through the main window when the tool window closes first.
		services.TryAddScoped<ClipboardClearSchedule>();
		services.TryAddScoped<IPendingClipboardClear>(provider => provider.GetRequiredService<ClipboardClearSchedule>());
		services.TryAddScoped<IClipboardService, JsClipboardService>();
		services.TryAddScoped<IUrlOpener, JsUrlOpener>();
		services.TryAddScoped<FormInterop>();
		return services;
	}
}
