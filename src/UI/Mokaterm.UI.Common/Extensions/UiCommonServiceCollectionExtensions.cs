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
		services.TryAddScoped<IClipboardService, JsClipboardService>();
		services.TryAddScoped<FormInterop>();
		return services;
	}
}
