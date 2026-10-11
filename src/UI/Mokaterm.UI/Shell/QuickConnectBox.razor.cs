using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Connections;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Opens an unsaved session from <c>user@host:port</c> or <c>scheme://user@host:port</c>, and offers saved logins, the
/// history and the names the picked protocol finds on this computer while it has the focus. The scheme wins over the
/// protocol picker, and typing one moves the picker. Ctrl+Enter saves the target as a login first.
/// </summary>
public sealed partial class QuickConnectBox : ComponentBase, IDisposable
{
	private const string PreferredProtocolId = "ssh";

	private const string SchemeSeparator = "://";

	private readonly string _listId = "mt-qc-list-" + Guid.NewGuid().ToString("N");
	private readonly string _hintId = "mt-qc-hint-" + Guid.NewGuid().ToString("N");
	private ElementReference _root;
	private IReadOnlyList<string> _protocolIds = [];
	private Dictionary<string, ProtocolDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
	private string _protocolId = PreferredProtocolId;
	private string _text = "";
	private string? _error;

	// Nothing below is read or kept until the box has the focus, and all of it goes when the focus does.
	private bool _focused;
	private bool _open;
	private int _activeIndex = -1;
	private int _generation;
	private List<QuickConnectRow> _rows = [];
	private IReadOnlyList<QuickConnectRecent> _recents = [];
	private IReadOnlyList<QuickConnectSuggestion> _detected = [];
	private string? _detectedFor;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private QuickConnectHistory History { get; set; } = default!;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	[Inject]
	private ShellInterop Interop { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<QuickConnectBox> Logger { get; set; } = default!;

	private bool IsOpen => _open && _rows.Count > 0;

	private QuickConnectRow? ActiveRow => IsOpen && _activeIndex >= 0 && _activeIndex < _rows.Count ? _rows[_activeIndex] : null;

	private string? ActiveOptionId => ActiveRow is null ? null : QuickConnectList.OptionId(_listId, _activeIndex);

	private string RootClass => _error is null ? "mt-quick-connect" : "mt-quick-connect mt-quick-connect--error";

	private string Placeholder => QuickConnectInput.Example(_descriptors.GetValueOrDefault(_protocolId));

	/// <summary>
	/// The keys the page keeps from the field for now, read by <c>attachComboboxKeys</c> in shell.js: Tab while it
	/// completes something, Shift+Delete while it forgets the active history row. The arrows are always kept.
	/// </summary>
	private string? KeptKeys
	{
		get
		{
			bool tab = CompletionRow() is { } row && !IsCompleted(row);
			bool delete = ActiveRow is { Kind: QuickConnectRowKind.Recent };
			return (tab, delete) switch
			{
				(true, true) => "tab del",
				(true, false) => "tab",
				(false, true) => "del",
				_ => null,
			};
		}
	}

	public void Dispose()
	{
		Shell.QuickConnectFocusRequested -= OnFocusRequested;
		History.Changed -= OnHistoryChanged;
		Catalog.Changed -= OnCatalogChanged;
	}

	protected override void OnInitialized()
	{
		_protocolIds = [.. Protocols.Protocols.Select(protocol => protocol.Id)];
		_descriptors = Protocols.Protocols.ToDictionary(protocol => protocol.Id, StringComparer.OrdinalIgnoreCase);
		if (!_protocolIds.Contains(PreferredProtocolId, StringComparer.OrdinalIgnoreCase) && _protocolIds.Count > 0)
		{
			_protocolId = _protocolIds[0];
		}

		Shell.QuickConnectFocusRequested += OnFocusRequested;
		History.Changed += OnHistoryChanged;
		Catalog.Changed += OnCatalogChanged;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await Interop.AttachComboboxKeysAsync(_root);
		}
	}

	/// <summary>The picker's own text: short, because the icon beside it already says most of it.</summary>
	private string ProtocolLabel(string protocolId) =>
		_descriptors.TryGetValue(protocolId, out ProtocolDescriptor? protocol) && protocol.DisplayName.Split(' ') is [var first, ..]
			? first.ToUpperInvariant()
			: protocolId.ToUpperInvariant();

	private async Task OnFocusAsync()
	{
		_focused = true;
		int generation = ++_generation;
		Task catalog = Catalog.EnsureLoadedAsync();
		IReadOnlyList<QuickConnectRecent> recents = await LoadRecentsAsync();
		await catalog;
		await LoadDetectedAsync(_protocolId, generation);
		if (generation != _generation)
		{
			return;
		}

		_recents = recents;
		Refresh(open: true);
	}

	private void OnBlur()
	{
		// A click on a row never gets here: the rows swallow the press that would move the focus.
		_focused = false;
		_generation++;
		_open = false;
		_activeIndex = -1;
		_error = null;
		_rows = [];
		_recents = [];
		_detected = [];
		_detectedFor = null;
	}

	private async Task OnTextChanged(string value)
	{
		_text = value ?? "";
		_error = null;
		_activeIndex = -1;
		string wanted = QuickConnectInput.ProtocolFor(_text, _protocolId, Protocols);
		bool moved = !string.Equals(wanted, _protocolId, StringComparison.OrdinalIgnoreCase);
		_protocolId = wanted;
		if (!_focused)
		{
			return;
		}

		Refresh(open: true);
		if (moved)
		{
			int generation = _generation;
			await LoadDetectedAsync(wanted, generation);
			if (generation == _generation)
			{
				Refresh();
			}
		}
	}

	private async Task OnProtocolPickedAsync(string protocolId)
	{
		_protocolId = protocolId;
		_error = null;

		// The scheme would win over the pick when connecting, so it goes rather than leave the two disagreeing.
		int scheme = _text.IndexOf(SchemeSeparator, StringComparison.Ordinal);
		if (QuickConnectTarget.ReadScheme(_text) is not null && scheme >= 0)
		{
			_text = _text[(scheme + SchemeSeparator.Length)..];
		}

		// Back to typing, which also opens the list with what the new protocol finds. A field that never lost the focus
		// gets no focus event, so the list is brought up to date here as well.
		await Forms.FocusPreferredAsync(_root);
		if (_focused)
		{
			await LoadDetectedAsync(protocolId, _generation);
			Refresh(open: true);
		}
	}

	private async Task OnKeyDownAsync(KeyboardEventArgs args)
	{
		if (args.IsComposing)
		{
			return;
		}

		switch (args.Key)
		{
			case "ArrowDown":
				Move(1);
				break;
			case "ArrowUp":
				Move(-1);
				break;
			case "Enter" when args.CtrlKey || args.MetaKey:
				await ConnectAndSaveAsync(ActiveRow);
				break;
			case "Enter" when !args.AltKey && !args.ShiftKey:
				if (ActiveRow is { } row)
				{
					await ActivateAsync(row);
				}
				else
				{
					ConnectTyped();
				}

				break;
			case "Escape":
				Escape();
				break;
			case "Tab" when !args.ShiftKey && !args.CtrlKey && !args.AltKey && !args.MetaKey:
				Complete();
				break;
			case "Delete" when args.ShiftKey:
				if (ActiveRow is { Kind: QuickConnectRowKind.Recent } recent)
				{
					await RemoveAsync(recent);
				}

				break;
		}
	}

	/// <summary>
	/// Moves the active row, opening the list first when it was closed. Past either end the arrows rest on the typed
	/// text itself, which is where Enter connects what was typed.
	/// </summary>
	private void Move(int step)
	{
		if (!_focused)
		{
			return;
		}

		if (!IsOpen)
		{
			Refresh(open: true);
			_activeIndex = IsOpen ? (step > 0 ? 0 : _rows.Count - 1) : -1;
			return;
		}

		int positions = _rows.Count + 1;
		_activeIndex = ((_activeIndex + 1 + step) % positions + positions) % positions - 1;
	}

	/// <summary>The first Escape closes the list or the hint, the second empties the box.</summary>
	private void Escape()
	{
		if (IsOpen || _error is not null)
		{
			_open = false;
			_activeIndex = -1;
			_error = null;
		}
		else if (_text.Length > 0)
		{
			_text = "";
			Refresh(open: false);
		}
	}

	/// <summary>
	/// Puts the active row's text in the box, or the best row's once something is typed, and keeps that row active so
	/// Enter opens it: a completed saved login still opens as the login, not as a quick connect to the same machine.
	/// </summary>
	private void Complete()
	{
		if (CompletionRow() is not { } row || IsCompleted(row))
		{
			return;
		}

		_text = row.Completion!;
		_error = null;
		if (_descriptors.TryGetValue(row.ProtocolId, out ProtocolDescriptor? protocol))
		{
			_protocolId = protocol.Id;
		}

		Refresh(open: true);
		_activeIndex = FindRow(row.Key);
	}

	private QuickConnectRow? CompletionRow() =>
		ActiveRow is { IsAction: false } active ? active
		: IsOpen && _text.Trim().Length > 0 ? _rows.FirstOrDefault(row => !row.IsAction)
		: null;

	private bool IsCompleted(QuickConnectRow row) =>
		string.Equals(row.Completion, _text.Trim(), StringComparison.Ordinal)
		&& string.Equals(row.ProtocolId, _protocolId, StringComparison.OrdinalIgnoreCase);

	private async Task ActivateAsync(QuickConnectRow row)
	{
		switch (row.Kind)
		{
			case QuickConnectRowKind.SavedLogin:
				Clear();
				await Sessions.OpenAsync(row.Connection!.Id, reuseExisting: true);
				break;
			case QuickConnectRowKind.Recent:
				Open(row.ProtocolId, row.Recent!.ToTarget());
				break;
			case QuickConnectRowKind.Detected:
				Open(row.ProtocolId, new QuickConnectTarget(row.ProtocolId, null, row.Suggestion!.Address, null));
				break;
			case QuickConnectRowKind.ConnectAndSave:
				await ConnectAndSaveAsync(null);
				break;
			case QuickConnectRowKind.ClearHistory:
				await ClearHistoryAsync();
				break;
		}
	}

	private void ConnectTyped()
	{
		if (_text.Trim().Length == 0)
		{
			return;
		}

		QuickConnectResolution resolution = QuickConnectInput.Resolve(_text, _protocolId, Protocols);
		if (!resolution.IsValid)
		{
			ShowProblem(resolution.Problem);
			return;
		}

		Open(resolution.Protocol.Id, resolution.Target);
	}

	private void Open(string protocolId, QuickConnectTarget target)
	{
		if (!_descriptors.TryGetValue(protocolId, out ProtocolDescriptor? protocol))
		{
			ShowProblem($"No installed module opens {protocolId}{SchemeSeparator} addresses.");
			return;
		}

		DateTimeOffset now = Time.GetUtcNow();
		(HostProfile host, ConnectionProfile connection) = QuickConnectInput.CreateProfiles(protocol, target, now);
		Clear();
		if (Sessions.OpenTransient(host, connection) is { } session)
		{
			History.RecordWhenConnected(session, QuickConnectRecent.From(protocol.Id, target, now));
		}
	}

	/// <summary>
	/// Opens the login editor on the target instead of a session; the editor connects once it saved. A history or
	/// found row is the target when one is active, and a saved login simply opens, since it is saved already.
	/// </summary>
	private async Task ConnectAndSaveAsync(QuickConnectRow? row)
	{
		QuickConnectResolution resolution;
		switch (row?.Kind)
		{
			case QuickConnectRowKind.SavedLogin:
				await ActivateAsync(row);
				return;
			case QuickConnectRowKind.Recent:
				resolution = QuickConnectInput.Resolve(row.Recent!.Text, row.ProtocolId, Protocols);
				break;
			case QuickConnectRowKind.Detected:
				resolution = QuickConnectInput.Resolve(row.Suggestion!.Address, row.ProtocolId, Protocols);
				break;
			default:
				if (_text.Trim().Length == 0)
				{
					return;
				}

				resolution = QuickConnectInput.Resolve(_text, _protocolId, Protocols);
				break;
		}

		if (!resolution.IsValid)
		{
			ShowProblem(resolution.Problem);
			return;
		}

		(HostProfile host, ConnectionProfile connection) = QuickConnectInput.CreateProfiles(resolution.Protocol, resolution.Target, Time.GetUtcNow());
		if (Shell.OpenEditor(new ConnectionEditorRequest { Draft = new QuickConnectDraft(host, connection) }))
		{
			Clear();
		}
	}

	private async Task RemoveAsync(QuickConnectRow row)
	{
		if (row.Recent is not { } recent)
		{
			return;
		}

		bool wasActive = ReferenceEquals(ActiveRow, row);
		int position = _activeIndex;
		try
		{
			await History.RemoveAsync(recent);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning("Forgetting a quick connect target failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Describe(ex, "forget this target"), "Quick connect");
			return;
		}

		_recents = [.. _recents.Where(entry => !entry.IsSameTarget(recent))];
		Refresh();

		// The row below moves up into the place of the one removed, so Shift+Delete can clear several in a row.
		if (wasActive && IsOpen)
		{
			_activeIndex = Math.Min(position, _rows.Count - 1);
		}
	}

	private async Task ClearHistoryAsync()
	{
		try
		{
			await History.ClearAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning("Clearing the quick connect history failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Describe(ex, "clear the history"), "Quick connect");
			return;
		}

		_recents = [];
		Refresh(open: true);
	}

	/// <summary>Picks the rows again from what is loaded, keeping the active row when it is still there.</summary>
	private void Refresh(bool? open = null)
	{
		string? activeKey = ActiveRow?.Key;
		List<QuickConnectRow> rows =
		[
			.. QuickConnectMatcher.Match(_text, _protocolId, new QuickConnectSources
			{
				Catalog = Catalog.Catalog,
				Recents = _recents,
				DetectedProtocolId = _detectedFor,
				Detected = _detected,
				Protocols = _descriptors,
			}),
		];

		string typed = _text.Trim();
		if (typed.Length > 0
			&& !rows.Exists(row => row is { Kind: QuickConnectRowKind.SavedLogin, Rank: QuickConnectRank.Exact })
			&& QuickConnectInput.Resolve(typed, _protocolId, Protocols).IsValid)
		{
			rows.Add(QuickConnectRow.ConnectAndSave());
		}

		if (typed.Length == 0 && rows.Exists(row => row.Kind == QuickConnectRowKind.Recent))
		{
			rows.Add(QuickConnectRow.ClearHistory());
		}

		_rows = rows;
		if (open is { } value)
		{
			_open = value;
		}

		_activeIndex = activeKey is null ? -1 : FindRow(activeKey);
	}

	private int FindRow(string key) => _rows.FindIndex(row => row.Key == key);

	private void ShowProblem(string? problem)
	{
		_error = problem;
		_open = false;
		_activeIndex = -1;
	}

	/// <summary>After a pick: the box empties and the list closes, and the picker stays where it was.</summary>
	private void Clear()
	{
		_text = "";
		_error = null;
		_open = false;
		_activeIndex = -1;
		_rows = [];
	}

	private async Task<IReadOnlyList<QuickConnectRecent>> LoadRecentsAsync()
	{
		try
		{
			IReadOnlyList<QuickConnectRecent> recents = await History.ListAsync();
			return [.. recents.Where(recent => recent.IsOpenable(Protocols))];
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning("Reading the quick connect history failed: {Failure}", LogSafe.Describe(ex));
			return [];
		}
	}

	/// <summary>
	/// Asks the protocol what it finds on this computer, once per focus. A provider reads rather than runs, but a
	/// registry or a folder can still be slow, so the read happens off the renderer's thread.
	/// </summary>
	private async Task LoadDetectedAsync(string protocolId, int generation)
	{
		if (string.Equals(_detectedFor, protocolId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		IReadOnlyList<QuickConnectSuggestion> found = [];
		if (Protocols.Find(protocolId) is IQuickConnectSuggestions suggestions)
		{
			try
			{
				found = await Task.Run(suggestions.GetQuickConnectSuggestions);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logger.LogWarning("Listing {Protocol} names for quick connect failed: {Failure}", protocolId, LogSafe.Describe(ex));
			}
		}

		if (generation == _generation)
		{
			_detected = found;
			_detectedFor = protocolId;
		}
	}

	private void OnFocusRequested() => _ = InvokeAsync(() => Forms.FocusPreferredAsync(_root, selectText: true).AsTask());

	private void OnHistoryChanged()
	{
		if (!_focused)
		{
			return;
		}

		_ = InvokeAsync(async () =>
		{
			int generation = _generation;
			IReadOnlyList<QuickConnectRecent> recents = await LoadRecentsAsync();
			if (generation == _generation && _focused)
			{
				_recents = recents;
				Refresh();
				StateHasChanged();
			}
		});
	}

	private void OnCatalogChanged()
	{
		if (_focused)
		{
			_ = InvokeAsync(() =>
			{
				if (_focused)
				{
					Refresh();
					StateHasChanged();
				}
			});
		}
	}
}
