using Microsoft.JSInterop;

namespace Mokaterm.Tests.Shared;

/// <summary>Stands in for the browser so the UI services a module registers can be built and validated.</summary>
internal sealed class FakeJsRuntime : IJSRuntime
{
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
		throw new NotSupportedException("The tests never call into JavaScript.");

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
		throw new NotSupportedException("The tests never call into JavaScript.");
}
