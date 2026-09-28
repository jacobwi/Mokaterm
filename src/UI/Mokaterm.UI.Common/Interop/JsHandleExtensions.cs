using Microsoft.JSInterop;

namespace Mokaterm.UI.Common.Interop;

/// <summary>Best-effort calls on JS handles for UI niceties that must never take the circuit down.</summary>
public static class JsHandleExtensions
{
	/// <summary>Calls <paramref name="identifier"/> on the handle. Returns false when the handle is missing, gone or the call failed.</summary>
	public static async ValueTask<bool> TryInvokeVoidAsync(this IJSObjectReference? handle, string identifier, params object?[] args)
	{
		if (handle is null)
		{
			return false;
		}

		try
		{
			await handle.InvokeVoidAsync(identifier, args);
			return true;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			return false;
		}
	}

	/// <summary>
	/// Runs the handle's own <c>dispose</c> and releases the reference. Both halves are best effort on purpose: a
	/// caller that skipped the second one over a failing script would leak its .NET reference for the page's lifetime.
	/// </summary>
	public static async ValueTask ReleaseAsync(this IJSObjectReference? handle)
	{
		if (handle is null)
		{
			return;
		}

		await handle.TryInvokeVoidAsync("dispose");
		try
		{
			await handle.DisposeAsync();
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// The circuit or WebView is already gone, and the listeners with it.
		}
	}
}
