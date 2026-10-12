using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// One page of the system view: it loads when shown, again on refresh and after a change it ran, and again when root
/// mode flips if it reads more as root. A newer load cancels the one before it, so a slow read cannot overwrite a
/// fresh one.
/// </summary>
/// <remarks>
/// Razor components are always public, so the page's state comes as a cascading value and every member that names a
/// module type is <c>private protected</c>: the pages see them, nothing outside the module does.
/// </remarks>
public abstract class AdminPageBase : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private CancellationTokenSource? _load;
	private PagePoller? _poller;
	private bool? _rootSeen;
	private bool _disposed;

	[CascadingParameter]
	private protected AdminPageContext Context { get; set; } = default!;

	private protected SystemAdminFeature Feature => Context.Feature;

	private protected SystemProfile Profile => Context.Profile;

	private protected SystemCapabilities Capabilities => Profile.Capabilities;

	/// <summary>A read is running. The first one shows a spinner; later ones keep the old data on screen.</summary>
	protected bool Loading { get; private set; }

	protected string? LoadError { get; private set; }

	/// <summary>A read finished at least once.</summary>
	protected bool Loaded { get; private set; }

	/// <summary>The page or the view is busy with a change, so its buttons are off.</summary>
	protected bool Busy => Context.IsBusy();

	/// <summary>The page reads more with root mode on (log lines, process names, password states).</summary>
	protected virtual bool ReadsMoreAsRoot => false;

	/// <summary>Reads as root when root mode allows it without asking.</summary>
	protected bool ReadAsRoot => Feature.CanActAsRoot;

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
		if (!disposing || _disposed)
		{
			return;
		}

		_disposed = true;
		_lifetime.Cancel();
		_poller?.Dispose();
		_load?.Cancel();
		_load?.Dispose();
		_load = null;
	}

	protected override async Task OnParametersSetAsync()
	{
		bool root = Feature.CanActAsRoot;
		bool first = _rootSeen is null;
		bool flipped = _rootSeen is { } seen && seen != root;
		_rootSeen = root;
		if (first || (flipped && ReadsMoreAsRoot))
		{
			await ReloadAsync();
		}
	}

	protected abstract Task LoadAsync(CancellationToken cancellationToken);

	protected async Task ReloadAsync()
	{
		if (_disposed)
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
		catch (AdminException ex) when (!load.IsCancellationRequested)
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

	/// <summary>
	/// A row's menu, from its menu button or a right click. It offers what the detail panel offers, so a row never
	/// carries a line of buttons of its own.
	/// </summary>
	protected void ShowRowMenu(MouseEventArgs args, IReadOnlyList<MokaContextMenuItem> items)
	{
		ArgumentNullException.ThrowIfNull(args);
		if (items.Count > 0)
		{
			Menu.Show(args, items);
		}
	}

	/// <summary>
	/// Calls <paramref name="tick"/> every <paramref name="interval"/> while the view is shown and the page is neither
	/// loading nor busy with a change. Replaces any polling already running.
	/// </summary>
	private protected void StartPolling(TimeSpan interval, Func<Task> tick)
	{
		if (_disposed)
		{
			return;
		}

		_poller ??= new PagePoller(Time, InvokeAsync, () => Context.IsVisible() && !Loading && !Busy);
		_poller.Start(interval, tick, _lifetime.Token);
	}

	private protected void StopPolling() => _poller?.Stop();

	/// <summary>Ends when the page goes away: what a polling tick reads under, since a tick has no load of its own to cancel.</summary>
	private protected CancellationToken Lifetime => _lifetime.Token;

	/// <summary>Reads <paramref name="script"/> and hands back its output, or throws with what went wrong.</summary>
	private protected async Task<AdminOutcome> ReadAsync(AdminScript script, CancellationToken cancellationToken) =>
		await Feature.ReadAsync(script, cancellationToken);

	/// <summary>
	/// Reviews and runs a change, then reloads the page. <paramref name="input"/> is disposed here whatever happens.
	/// </summary>
	private protected async Task<AdminActionResult?> RunAsync(AdminAction action, AdminInput? input = null)
	{
		using (input)
		{
			AdminActionResult? result = await Context.Run(action, input);
			if (result is not null && !_disposed)
			{
				await ReloadAsync();
			}

			return result;
		}
	}
}
