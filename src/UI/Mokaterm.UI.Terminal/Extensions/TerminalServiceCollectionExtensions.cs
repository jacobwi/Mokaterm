using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Terminal.Interop;

namespace Mokaterm.UI.Terminal.Extensions;

public static class TerminalServiceCollectionExtensions
{
	/// <summary>Registers services the terminal view needs. Called by the shell's <c>AddMokatermUI()</c>; safe to call twice.</summary>
	public static IServiceCollection AddMokatermTerminal(this IServiceCollection services)
	{
		// The view also needs IClipboardService from UI.Common; that registration is idempotent too.
		services.AddMokatermUiCommon();
		services.TryAddScoped<TerminalInterop>();
		return services;
	}
}
