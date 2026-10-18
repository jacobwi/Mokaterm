using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// The rows under the quick connect box. It only draws them: the box owns the keys, the active row and what a pick
/// does, so the list never takes the focus from it.
/// </summary>
public sealed partial class QuickConnectList : ComponentBase
{
	/// <summary>The listbox's id, which the box names in <c>aria-controls</c>; each row's id is built from it.</summary>
	[Parameter, EditorRequired]
	public string Id { get; set; } = "";

	[Parameter, EditorRequired]
	public IReadOnlyList<QuickConnectRow> Rows { get; set; } = [];

	/// <summary>The row the arrow keys are on, or -1 for none.</summary>
	[Parameter]
	public int ActiveIndex { get; set; } = -1;

	[Parameter]
	public EventCallback<QuickConnectRow> OnPick { get; set; }

	[Parameter]
	public EventCallback<QuickConnectRow> OnRemove { get; set; }

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	/// <summary>The id of the row at <paramref name="index"/>, for the box's <c>aria-activedescendant</c>.</summary>
	public static string OptionId(string listId, int index) =>
		string.Create(CultureInfo.InvariantCulture, $"{listId}-{index}");

	private static string RowClass(QuickConnectRow row, bool active) =>
		(row.IsAction ? "mt-qc-row mt-qc-row--action" : "mt-qc-row")
		+ (row.Kind == QuickConnectRowKind.ClearHistory ? " mt-qc-row--quiet" : "")
		+ (active ? " mt-qc-row--active" : "");

	private string ProtocolName(QuickConnectRow row) => Protocols.DisplayName(row.ProtocolId);

	/// <summary>What a screen reader says for the row: the target, then what it is.</summary>
	private string AccessibleName(QuickConnectRow row) => row.Kind switch
	{
		QuickConnectRowKind.SavedLogin when SessionTitle.Name(row.Host!, row.Connection!) is { } name =>
			$"{name}, {row.Completion}, saved {ProtocolName(row)} login",
		QuickConnectRowKind.SavedLogin => $"{row.Completion}, saved {ProtocolName(row)} login",
		QuickConnectRowKind.Recent => $"{row.Completion}, recent {ProtocolName(row)} connection",
		QuickConnectRowKind.Detected when row.Suggestion!.Description is { Length: > 0 } description =>
			$"{row.Suggestion.Address}, {description}, {ProtocolName(row)}",
		QuickConnectRowKind.Detected => $"{row.Suggestion!.Address}, {ProtocolName(row)}",
		QuickConnectRowKind.ConnectAndSave => "Connect and save as a login",
		_ => "Clear history",
	};

	/// <summary>The whole target with its protocol, for a row whose label was cut short.</summary>
	private string? Tooltip(QuickConnectRow row) => row.Kind switch
	{
		QuickConnectRowKind.SavedLogin or QuickConnectRowKind.Recent or QuickConnectRowKind.Detected =>
			$"{ProtocolName(row)}: {row.Completion}",
		_ => null,
	};
}
