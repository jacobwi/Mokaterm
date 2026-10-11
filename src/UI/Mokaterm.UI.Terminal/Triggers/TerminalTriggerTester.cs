using Microsoft.JSInterop;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Terminal.Interop;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>
/// Runs trigger rules on a sample line in the page, through the same script a terminal runs them with. The settings
/// page checks a pattern here as it is typed: whether JavaScript accepts it is something only the page can say, and .NET's
/// own ECMAScript mode would accept patterns the browser refuses and refuse some it accepts.
/// </summary>
public sealed class TerminalTriggerTester : IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.Terminal/js/triggers.js";

	private readonly JsModule _module;

	public TerminalTriggerTester(IJSRuntime jsRuntime) => _module = new JsModule(jsRuntime, ModulePath);

	/// <summary>One result per rule, in order. Only the pattern and the case option of a rule matter here.</summary>
	public async ValueTask<IReadOnlyList<TriggerTestResult>> TestAsync(
		IReadOnlyList<TerminalTrigger> rules,
		string sample,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(rules);
		TerminalJsTrigger[] payload = [.. rules.Select(TerminalJsTrigger.From)];
		TriggerTestResult[]? results = await _module.InvokeAsync<TriggerTestResult[]?>("testTriggers", cancellationToken, payload, sample ?? "");
		return results ?? [];
	}

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
