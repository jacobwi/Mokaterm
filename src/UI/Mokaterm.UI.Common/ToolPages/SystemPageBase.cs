using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Mokaterm.Abstractions.ServerAdmin;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// One page of the System view, built in or from a plugin: it loads when shown, again on refresh and after a change it
/// ran, and again when root mode flips if it reads more as root. A newer load cancels the one before it, so a slow read
/// cannot overwrite a fresh one. Register it with <c>AddSystemPage</c>; draw it on a <see cref="ToolPageFrame"/>.
/// </summary>
public abstract class SystemPageBase : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private CancellationTokenSource? _load;
	private PagePoller? _poller;
	private bool? _rootSeen;

	/// <summary>The view around the page.</summary>
	[CascadingParameter]
	protected ISystemPageContext Context { get; set; } = default!;

	/// <summary>A read is running. The first one shows a spinner; later ones keep the old data on screen.</summary>
	protected bool Loading { get; private set; }

	protected string? LoadError { get; private set; }

	/// <summary>A read finished at least once.</summary>
	protected bool Loaded { get; private set; }

	/// <summary>The page or the view is busy with a change, so its buttons are off.</summary>
	protected bool Busy => Context.IsBusy;

	/// <summary>The page reads more with root mode on (log lines, process names, password states), so it reads again when root mode flips.</summary>
	protected virtual bool ReadsMoreAsRoot => false;

	/// <summary>Reads as root when root mode allows it without asking.</summary>
	protected bool ReadAsRoot => Context.CanReadAsRoot;

	/// <summary>The page went away; nothing it started should touch it any more.</summary>
	protected bool IsDisposed { get; private set; }

	/// <summary>Ends when the page goes away: what a polling tick reads under, since a tick has no load of its own to cancel.</summary>
	protected CancellationToken Lifetime => _lifetime.Token;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposing || IsDisposed)
		{
			return;
		}

		IsDisposed = true;
		_lifetime.Cancel();
		_poller?.Dispose();
		_load?.Cancel();
		_load?.Dispose();
		_load = null;
	}

	protected override async Task OnParametersSetAsync()
	{
		bool root = Context.CanReadAsRoot;
		bool first = _rootSeen is null;
		bool flipped = _rootSeen is { } seen && seen != root;
		_rootSeen = root;
		if (first || (flipped && ReadsMoreAsRoot))
		{
			await ReloadAsync();
		}
	}

	/// <summary>Reads what the page shows. A <see cref="SystemScriptException"/> lands in <see cref="LoadError"/>.</summary>
	protected abstract Task LoadAsync(CancellationToken cancellationToken);

	protected async Task ReloadAsync()
	{
		if (IsDisposed)
		{
			return;
		}

		_load?.Cancel();
		_load?.Dispose();
		CancellationTokenSource load = new();
		_load = load;
		Loading = true;
		LoadError = null;
		StateHasChanged();
		try
		{
			await LoadAsync(load.Token);
			Loaded = true;
		}
		catch (SystemScriptException ex) when (!load.IsCancellationRequested)
		{
			LoadError = ex.Message;
		}
		catch (OperationCanceledException) when (load.IsCancellationRequested)
		{
			return;
		}

		if (ReferenceEquals(_load, load))
		{
			Loading = false;
		}
	}

	/// <summary>Runs a read through the view; see <see cref="ISystemPageContext.ReadAsync"/>.</summary>
	protected Task<SystemScriptResult> ReadAsync(SystemScript script, CancellationToken cancellationToken) => Context.ReadAsync(script, cancellationToken);

	/// <summary>Reviews and runs a change through the view, then reads the page again once it ran. Null when the user cancelled.</summary>
	protected async Task<SystemChangeResult?> RunChangeAsync(SystemChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		SystemChangeResult? result = await Context.RunChangeAsync(change);
		await ReloadAfterChangeAsync(result is not null);
		return result;
	}

	/// <summary>Reads the page again after a change that ran, unless the page is gone.</summary>
	protected async Task ReloadAfterChangeAsync(bool ran)
	{
		if (ran && !IsDisposed)
		{
			await ReloadAsync();
		}
	}

	/// <summary>
	/// A row's menu, from its menu button or a right click. It offers what the detail panel offers, so a row never
	/// carries a line of buttons of its own.
	/// </summary>
	protected void ShowRowMenu(MouseEventArgs args, IReadOnlyList<MokaContextMenuItem> items)
	{
		ArgumentNullException.ThrowIfNull(args);
		ArgumentNullException.ThrowIfNull(items);
		if (items.Count > 0)
		{
			Menu.Show(args, items);
		}
	}

	/// <summary>
	/// Calls <paramref name="tick"/> every <paramref name="interval"/> while the view is shown and the page is neither
	/// loading nor busy with a change. Replaces any polling already running.
	/// </summary>
	protected void StartPolling(TimeSpan interval, Func<Task> tick)
	{
		if (IsDisposed)
		{
			return;
		}

		_poller ??= new PagePoller(Time, InvokeAsync, () => Context.IsVisible && !Loading && !Busy);
		_poller.Start(interval, tick, _lifetime.Token);
	}

	protected void StopPolling() => _poller?.Stop();
}
