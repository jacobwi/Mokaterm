using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Opening a web address from .NET: only http and https reach the page, where the shared interop script opens them, and a
/// page that is gone answers false instead of throwing at the caller.
/// </summary>
public sealed class UrlOpenerTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("https://grafana.example.com/d/abc", "https://grafana.example.com/d/abc")]
	[InlineData("http://10.10.2.3:8080", "http://10.10.2.3:8080/")]
	public async Task AWebAddress_IsOpenedThroughTheSharedScript(string address, string opened)
	{
		ScriptedPage page = new();
		await using JsUrlOpener opener = new(page);

		bool result = await opener.OpenAsync(address, Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.True(result);
		Assert.Equal(["./_content/Mokaterm.UI.Common/js/interop.js"], page.Imported);
		Assert.Equal([("openWebLink", opened)], page.Module.Calls);
	}

	[Theory]
	[InlineData("file:///etc/passwd")]
	[InlineData("javascript:alert(1)")]
	[InlineData("ms-settings:privacy")]
	[InlineData("example.com")]
	[InlineData("")]
	public async Task AnythingElse_IsRefusedBeforeThePageIsAsked(string address)
	{
		ScriptedPage page = new();
		await using JsUrlOpener opener = new(page);

		Assert.False(await opener.OpenAsync(address, Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct));
		Assert.Empty(page.Imported);
	}

	[Fact]
	public async Task APageThatIsGone_AnswersFalse()
	{
		ScriptedPage page = new() { Gone = true };
		await using JsUrlOpener opener = new(page);

		Assert.False(await opener.OpenAsync("https://example.com", Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct));
	}

	[Fact]
	public void TheOpenerIsRegisteredPerPage()
	{
		ServiceCollection services = new();
		services.AddMokatermUiCommon();

		ServiceDescriptor opener = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IUrlOpener));
		Assert.Equal(ServiceLifetime.Scoped, opener.Lifetime);
	}

	/// <summary>A page whose script answers <c>openWebLink</c> the way interop.js does for an address it accepts.</summary>
	private sealed class ScriptedPage : IJSRuntime
	{
		public List<string> Imported { get; } = [];

		public ScriptedModule Module { get; } = new();

		public bool Gone { get; init; }

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			if (Gone)
			{
				return ValueTask.FromException<TValue>(new JSDisconnectedException("The circuit is gone."));
			}

			Assert.Equal("import", identifier);
			Imported.Add((string)args![0]!);
			return ValueTask.FromResult((TValue)(object)Module);
		}
	}

	private sealed class ScriptedModule : IJSObjectReference
	{
		public List<(string Identifier, string Address)> Calls { get; } = [];

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			Calls.Add((identifier, (string)args![0]!));
			return ValueTask.FromResult((TValue)(object)true);
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
