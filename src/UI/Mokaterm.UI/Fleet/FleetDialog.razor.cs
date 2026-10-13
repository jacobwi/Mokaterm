using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Data.Table;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// The fleet overview: one row per saved login that can report on its machine, with what the window last read from it.
/// It is a shell dialog rather than a workspace tab because every tab in the workspace is a session, with a pane, a
/// restore entry and a toolbar, and this is none of those; what it shows lives in <see cref="FleetOverview"/>, so
/// closing it to run a command or open a tab loses nothing, and opening it again shows the same rows.
/// </summary>
public sealed partial class FleetDialog : ShellEditorBase<FleetEditorRequest>
{
	private static readonly string[] SearchSeparators = [" "];

	private readonly HashSet<Guid> _picked = [];
	private readonly HashSet<Guid> _expanded = [];
	private IReadOnlyList<FleetRow> _rows = [];
	private IReadOnlyList<Guid>? _held;
	private string _filter = "";
	private FleetView _view = FleetView.All;
	private string? _error;
	private bool _refreshing;
	private bool _retrying;
	private bool _subscribed;
	private int _renderQueued;
	private ElementReference _table;
	private FleetEditorRequest? _keysAttachedFor;

	private enum FleetView
	{
		All,
		Attention,
	}

	[Inject]
	private FleetOverview Fleet { get; set; } = default!;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private ShellInterop Interop { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<FleetDialog> Logger { get; set; } = default!;

	/// <summary>Only while a refresh is being handed to the survey; a read that is going may be left behind, and goes on.</summary>
	protected override bool IsBusy => _refreshing;

	/// <summary>The rows the view and the filter let through, in the chosen order, or in the held one while the pointer is on the table.</summary>
	private IReadOnlyList<FleetRow> Visible
	{
		get
		{
			IReadOnlyList<FleetRow> sorted = FleetSort.Order(Filtered, Fleet.Sort);
			return _held is null ? sorted : FleetSort.Hold(sorted, _held);
		}
	}

	private List<FleetRow> Filtered
	{
		get
		{
			string[] words = _filter.Split(SearchSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			return
			[
				.. _rows.Where(row =>
					(_view == FleetView.All || FleetAttention.Reasons(row).Count > 0)
					&& words.All(word => SearchText(row).Contains(word, StringComparison.CurrentCultureIgnoreCase))),
			];
		}
	}

	private List<FleetRow> PickedRows => [.. _rows.Where(row => _picked.Contains(row.Login.Id))];

	private IReadOnlyList<EnumOption<FleetView>> Views =>
	[
		new(FleetView.All, string.Create(CultureInfo.CurrentCulture, $"All ({_rows.Count})")),
		new(FleetView.Attention, string.Create(CultureInfo.CurrentCulture, $"Needs a look ({_rows.Count(row => FleetAttention.Reasons(row).Count > 0)})")),
	];

	private int SkippedCount => _rows.Count(row => row.Result?.State == HostRunState.Skipped);

	private int RetryableCount => _rows.Count(row => row.Result?.State is HostRunState.Failed or HostRunState.Cancelled);

	private string Summary
	{
		get
		{
			List<string> parts = [_rows.Count == 1 ? "1 login" : string.Create(CultureInfo.CurrentCulture, $"{_rows.Count} logins")];
			Add(_rows.Count(row => row.Result?.State == HostRunState.Done), "read");
			Add(_rows.Count(row => row.IsPending), "reading");
			Add(SkippedCount, "skipped");
			Add(_rows.Count(row => row.Result?.State == HostRunState.Failed), "failed");
			Add(_rows.Count(row => row.Result?.State == HostRunState.Cancelled), "cancelled");
			return string.Join(" · ", parts);

			void Add(int count, string what)
			{
				if (count > 0)
				{
					parts.Add(string.Create(CultureInfo.CurrentCulture, $"{count} {what}"));
				}
			}
		}
	}

	private string PickedSummary => _picked.Count == 0 ? "Pick rows to act on several at once" : string.Create(CultureInfo.CurrentCulture, $"{PickedRows.Count} picked");

	private string NotReadText
	{
		get
		{
			int parallel = Settings.Get<CommandRunSettings>().Clamped().ParallelHosts;
			return string.Create(
				CultureInfo.CurrentCulture,
				$"Nothing read yet. Refresh all connects to each login without opening a tab, {parallel} at a time, and skips any that would ask for a password, a passphrase or a host key. Nothing it reads is saved.");
		}
	}

	protected override async Task OnInitializedAsync()
	{
		Fleet.Changed += OnFleetChanged;
		Catalog.Changed += OnFleetChanged;
		_subscribed = true;
		await base.OnInitializedAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		await base.OnAfterRenderAsync(firstRender);

		// The table is new with every opening, so the row keys go on again each time it appears.
		if (Request is { } request && !ReferenceEquals(_keysAttachedFor, request) && !string.IsNullOrEmpty(_table.Id))
		{
			_keysAttachedFor = request;
			_ = await Interop.AttachRowKeysAsync(_table);
		}
	}

	protected override async Task LoadAsync(FleetEditorRequest request)
	{
		await Catalog.EnsureLoadedAsync();
		if (IsCurrent(request))
		{
			_rows = Fleet.Rows();
		}
	}

	protected override void Reset()
	{
		_picked.Clear();
		_expanded.Clear();
		_held = null;
		_filter = "";
		_view = FleetView.All;
		_error = null;
		_refreshing = false;
		_retrying = false;
		_keysAttachedFor = null;
		_table = default;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _subscribed)
		{
			_subscribed = false;
			Fleet.Changed -= OnFleetChanged;
			Catalog.Changed -= OnFleetChanged;
		}

		base.Dispose(disposing);
	}

	private static string SearchText(FleetRow row) => string.Join(
		' ',
		[row.Login.GetTitle(row.Host), row.Host.DisplayName, row.Host.Address, row.Login.Username ?? "", row.Summary?.OperatingSystem ?? "", row.Summary?.HostName ?? "", .. row.Host.Tags]);

	/// <summary>Coalesces a burst of host changes into one render: forty machines answering together is one redraw, not forty.</summary>
	private void OnFleetChanged()
	{
		if (Interlocked.Exchange(ref _renderQueued, 1) == 1)
		{
			return;
		}

		_ = InvokeAsync(() =>
		{
			Volatile.Write(ref _renderQueued, 0);
			if (Request is null)
			{
				return;
			}

			_rows = Fleet.Rows();

			// A login deleted while the dialog is open leaves the picks with it.
			HashSet<Guid> listed = [.. _rows.Select(row => row.Login.Id)];
			_picked.IntersectWith(listed);
			_expanded.IntersectWith(listed);
			StateHasChanged();
		});
	}

	private void SetView(FleetView view)
	{
		_view = view;
		Rehold();
	}

	private void OnFilterChanged(string filter)
	{
		_filter = filter;
		Rehold();
	}

	private MokaSortDirection DirectionOf(FleetColumn column) => FleetSort.DirectionOf(Fleet.Sort, column);

	/// <summary>The window keeps the order, so the next opening of the dialog shows the table sorted the same way.</summary>
	private void SortBy(FleetColumn column)
	{
		Fleet.Sort = FleetSort.Next(Fleet.Sort, column);
		Rehold();
	}

	private void HoldOrder() => _held ??= [.. Visible.Select(row => row.Login.Id)];

	private void ReleaseOrder() => _held = null;

	/// <summary>
	/// A sort, a filter or a view the user picked applies at once, even with the pointer on the table: it is the user's
	/// own reordering, not one that arrived under the pointer. What it shows is then what holds.
	/// </summary>
	private void Rehold()
	{
		if (_held is not null)
		{
			_held = null;
			HoldOrder();
		}
	}

	private void TogglePick(Guid connectionId)
	{
		if (!_picked.Remove(connectionId))
		{
			_ = _picked.Add(connectionId);
		}
	}

	private void ToggleExpand(Guid connectionId)
	{
		if (!_expanded.Remove(connectionId))
		{
			_ = _expanded.Add(connectionId);
		}
	}

	private void PickShown() => _picked.UnionWith(Visible.Select(row => row.Login.Id));

	private void ClearPicked() => _picked.Clear();

	private Task RefreshAllAsync() => RefreshAsync(null);

	private Task RefreshPickedAsync() => RefreshAsync([.. PickedRows.Select(row => row.Login.Id)]);

	private async Task RefreshAsync(IReadOnlyCollection<Guid>? connectionIds)
	{
		if (_refreshing)
		{
			return;
		}

		_refreshing = true;
		_error = null;
		try
		{
			_ = await Fleet.RefreshAsync(connectionIds);
			_rows = Fleet.Rows();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError("Reading the fleet failed: {Failure}", LogSafe.Describe(ex));
			_error = VaultFailure.Describe(ex, "read the machines");
		}
		finally
		{
			_refreshing = false;
		}
	}

	private Task RunSkippedAskingAsync() => RetryAsync(Fleet.RunSkippedAskingAsync);

	private Task RetryFailedAsync() => RetryAsync(Fleet.RetryFailedAsync);

	private async Task RetryAsync(Func<Task<int>> retry)
	{
		if (_retrying)
		{
			return;
		}

		_retrying = true;
		_error = null;
		try
		{
			_ = await retry();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError("Reading the fleet again failed: {Failure}", LogSafe.Describe(ex));
			_error = VaultFailure.Describe(ex, "read the machines again");
		}
		finally
		{
			_retrying = false;
		}
	}

	private void Stop() => Fleet.Stop();

	/// <summary>The command run is a dialog of its own and only one shows at a time, so this one steps aside; its rows stay with the window.</summary>
	private void RunCommand(IReadOnlyList<Guid> connectionIds)
	{
		CloseSaved();
		_ = OpenEditor(new CommandRunEditorRequest { ConnectionIds = connectionIds });
	}

	private void RunCommandOnPicked() => RunCommand([.. PickedRows.Select(row => row.Login.Id)]);

	private Task OpenPickedAsync() => OpenAsync([.. PickedRows.Select(row => row.Login.Id)]);

	/// <summary>A tab opens behind this dialog, where nobody would see it, so the dialog closes first.</summary>
	private async Task OpenAsync(IReadOnlyList<Guid> connectionIds)
	{
		CloseSaved();
		foreach (Guid connectionId in connectionIds)
		{
			await Sessions.OpenAsync(connectionId, reuseExisting: true);
		}
	}
}
