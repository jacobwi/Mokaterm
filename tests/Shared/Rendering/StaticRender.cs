using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Renders a component to HTML with Blazor's static renderer. Lifecycle methods and error boundaries run for real; the
/// browser never does, so after-render interop and event handlers stay out of reach.
/// </summary>
internal static class StaticRender
{
	public static async Task<string> RenderAsync<TComponent>(Action<IServiceCollection> configure, Dictionary<string, object?> parameters)
		where TComponent : IComponent
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddScoped<IJSRuntime, IdleJsRuntime>();
		configure(services);
		await using ServiceProvider provider = services.BuildServiceProvider();
		await using AsyncServiceScope scope = provider.CreateAsyncScope();
		await using HtmlRenderer renderer = new(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());

		return await renderer.Dispatcher.InvokeAsync(async () =>
		{
			HtmlRootComponent component = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
			return component.ToHtmlString();
		});
	}

	/// <summary>
	/// Static rendering never reaches a browser. Like the runtime Blazor itself uses there, every call throws
	/// <see cref="InvalidOperationException"/>, which components (Moka.Red's dialog among them) read as "no JavaScript yet".
	/// </summary>
	private sealed class IdleJsRuntime : IJSRuntime
	{
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			throw NoJavaScript(identifier);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
			throw NoJavaScript(identifier);

		private static InvalidOperationException NoJavaScript(string identifier) =>
			new($"JavaScript interop calls cannot be issued during static rendering ({identifier}).");
	}
}
