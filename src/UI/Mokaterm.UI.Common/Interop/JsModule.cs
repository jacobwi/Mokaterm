using Microsoft.JSInterop;

namespace Mokaterm.UI.Common.Interop;

/// <summary>
/// A lazily imported ES module. Services and components share this instead of each re-implementing import,
/// caching and teardown. The <c>TryInvoke</c> variants swallow the errors a closing circuit or WebView throws.
/// </summary>
public sealed class JsModule : IAsyncDisposable
{
	private readonly IJSRuntime _jsRuntime;
	private readonly string _path;
	private readonly SemaphoreSlim _importLock = new(1, 1);
	private IJSObjectReference? _module;
	private bool _disposed;

	/// <param name="path">Static web asset path, for example <c>./_content/Mokaterm.UI.Terminal/js/terminal.js</c>.</param>
	public JsModule(IJSRuntime jsRuntime, string path)
	{
		_jsRuntime = jsRuntime;
		_path = path;
	}

	public async ValueTask InvokeVoidAsync(string identifier, CancellationToken cancellationToken, params object?[]? args)
	{
		IJSObjectReference module = await GetModuleAsync(cancellationToken);
		await module.InvokeVoidAsync(identifier, cancellationToken, args);
	}

	public ValueTask InvokeVoidAsync(string identifier, params object?[]? args) =>
		InvokeVoidAsync(identifier, CancellationToken.None, args);

	public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, params object?[]? args)
	{
		IJSObjectReference module = await GetModuleAsync(cancellationToken);
		return await module.InvokeAsync<T>(identifier, cancellationToken, args);
	}

	public ValueTask<T> InvokeAsync<T>(string identifier, params object?[]? args) =>
		InvokeAsync<T>(identifier, CancellationToken.None, args);

	/// <summary>Like <see cref="InvokeVoidAsync(string, object?[])"/> but returns false instead of throwing when the JS side is gone.</summary>
	public async ValueTask<bool> TryInvokeVoidAsync(string identifier, params object?[]? args)
	{
		try
		{
			await InvokeVoidAsync(identifier, CancellationToken.None, args);
			return true;
		}
		catch (Exception ex) when (IsTeardown(ex))
		{
			return false;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		IJSObjectReference? module = Interlocked.Exchange(ref _module, null);
		if (module is not null)
		{
			try
			{
				await module.DisposeAsync();
			}
			catch (Exception ex) when (IsTeardown(ex))
			{
				// The circuit or WebView is already gone; nothing left to release.
			}
		}

		_importLock.Dispose();
	}

	/// <summary>
	/// True for the exceptions JS interop throws while a circuit or WebView shuts down. The narrow tier: a
	/// <see cref="JSException"/> is a script that threw, which is a bug worth surfacing, so it is not in here.
	/// </summary>
	public static bool IsTeardown(Exception exception) =>
		exception is JSDisconnectedException or ObjectDisposedException or OperationCanceledException;

	/// <summary>
	/// True for teardown and for a <see cref="JSException"/>. The wide tier, for a call whose failure must never reach
	/// the caller: a page sink that feeds one view, or a UI nicety that has already done its job by the time the
	/// script fails. Everything else uses <see cref="IsTeardown"/> and lets a broken script show.
	/// </summary>
	public static bool IsExpected(Exception exception) => IsTeardown(exception) || exception is JSException;

	private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_module is not null)
		{
			return _module;
		}

		await _importLock.WaitAsync(cancellationToken);
		try
		{
			return _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, _path);
		}
		finally
		{
			_importLock.Release();
		}
	}
}
