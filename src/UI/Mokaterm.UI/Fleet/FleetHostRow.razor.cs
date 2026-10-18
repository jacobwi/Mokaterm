using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// One login of the fleet overview. It draws again only when its row, its pick or its fold changed, because a survey
/// of forty machines replaces one row at a time.
/// </summary>
public sealed partial class FleetHostRow : ComponentBase
{
	/// <summary>What a column shows for a value the machine did not give.</summary>
	private const string Unknown = "-";

	private FleetRow? _rendered;
	private bool _renderedPicked;
	private bool _renderedExpanded;
	private DateTimeOffset _renderedNow;

	[Parameter, EditorRequired]
	public FleetRow Row { get; set; } = default!;

	[Parameter]
	public bool Picked { get; set; }

	[Parameter]
	public bool Expanded { get; set; }

	/// <summary>The time the row's age is measured against, so every row of one render says the same "now".</summary>
	[Parameter]
	public DateTimeOffset Now { get; set; }

	[Parameter]
	public EventCallback OnTogglePick { get; set; }

	[Parameter]
	public EventCallback OnToggleExpand { get; set; }

	[Parameter]
	public EventCallback OnRefresh { get; set; }

	[Parameter]
	public EventCallback OnOpen { get; set; }

	[Parameter]
	public EventCallback OnRunCommand { get; set; }

	private HostSummary? Summary => Row.Summary;

	private string RowClass
	{
		get
		{
			string css = "mt-fleet-row";
			if (Picked)
			{
				css += " mt-fleet-row--picked";
			}

			if (Row.Result?.IsProblem == true)
			{
				css += " mt-fleet-row--problem";
			}

			// A summary kept from an earlier read while this one failed or was skipped is old news, and looks it.
			if (Row.Result is { State: not HostRunState.Done, Summary: not null } && !Row.IsPending)
			{
				css += " mt-fleet-row--stale";
			}

			return css;
		}
	}

	private string StateLabel => Row.Result is { } result ? HostRunDisplay.Label(result.State) : "NOT READ";

	private MokaColor StateColor => Row.Result is { } result ? HostRunDisplay.Color(result) : MokaColor.Surface;

	/// <summary>The row's reasons for a look, or what it is doing, as the hint on its state.</summary>
	private string? Tooltip
	{
		get
		{
			IReadOnlyList<string> reasons = FleetAttention.Reasons(Row);
			if (reasons.Count > 0)
			{
				return string.Join(" · ", reasons);
			}

			return Row.IsPending ? Row.Result?.Message : null;
		}
	}

	private string LoadClass => Summary?.LoadPercent is >= 100
		? "mt-fleet-cell mt-fleet-cell--count mt-fleet-cell--load mt-fleet-mono mt-fleet-alert"
		: "mt-fleet-cell mt-fleet-cell--count mt-fleet-cell--load mt-fleet-mono";

	private string? LoadText => Summary is { Load1: { } one } summary
		? string.Create(
			CultureInfo.InvariantCulture,
			$"{one:0.00} {summary.Load5:0.00} {summary.Load15:0.00}{(summary.CpuCount is { } cpus ? $" on {cpus} CPUs" : "")}")
		: null;

	private string? MemoryText => Summary is { MemoryUsedBytes: { } used, MemoryTotalBytes: { } total }
		? $"{DisplayFormat.Bytes(used)} of {DisplayFormat.Bytes(total)}"
		: null;

	private string? DiskText => Summary?.FullestDisk is { } disk
		? $"{disk.MountPoint}: {DisplayFormat.Bytes(disk.UsedBytes)} of {DisplayFormat.Bytes(disk.TotalBytes)}"
		: null;

	private string? UpdatesText => Summary?.PendingUpdates switch
	{
		null => null,
		0 => "None pending",
		int pending => Summary.SecurityUpdates is { } security
			? string.Create(CultureInfo.CurrentCulture, $"{pending} pending, {security} for security")
			: string.Create(CultureInfo.CurrentCulture, $"{pending} pending"),
	};

	private string? FailedText => Summary?.FailedServiceNames is { Count: > 0 } names ? string.Join(", ", names) : null;

	private string? ContainersText => Summary is { ContainersRunning: { } running, ContainersTotal: { } total }
		? string.Create(CultureInfo.CurrentCulture, $"{running} running of {total}")
		: null;

	private string ReadText
	{
		get
		{
			if (Row.Result is not { ReadAt: { } readAt } result)
			{
				return "";
			}

			string age = DisplayFormat.Relative(readAt, Now);
			return result.State == HostRunState.Done && result.Duration is { } duration ? $"{age}, in {HostRunDisplay.Duration(duration)}" : age;
		}
	}

	protected override bool ShouldRender()
	{
		// Rows are built afresh for every render of the list, so what counts is whether their parts were replaced.
		if (_rendered is { } before
			&& ReferenceEquals(before.Result, Row.Result)
			&& ReferenceEquals(before.Host, Row.Host)
			&& ReferenceEquals(before.Login, Row.Login)
			&& _renderedPicked == Picked
			&& _renderedExpanded == Expanded
			&& (!Expanded || _renderedNow == Now))
		{
			return false;
		}

		_rendered = Row;
		_renderedPicked = Picked;
		_renderedExpanded = Expanded;
		_renderedNow = Now;
		return true;
	}

	private static string Percent(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0}%");

	private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

	private async Task OnKeyDownAsync(KeyboardEventArgs args)
	{
		if (args.CtrlKey || args.AltKey || args.MetaKey)
		{
			return;
		}

		switch (args.Key)
		{
			case " ":
				await OnTogglePick.InvokeAsync();
				break;
			case "Enter":
				await OnToggleExpand.InvokeAsync();
				break;
			case "r" or "R" when !Row.IsPending:
				await OnRefresh.InvokeAsync();
				break;
			case "o" or "O":
				await OnOpen.InvokeAsync();
				break;
		}
	}
}
