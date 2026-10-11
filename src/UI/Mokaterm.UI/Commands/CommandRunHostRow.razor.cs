using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Commands;

/// <summary>
/// One host of a command run. It draws again only when its row was replaced or folded, because a run of forty hosts
/// changes one row at a time and each row can carry hundreds of kilobytes of output.
/// </summary>
public sealed partial class CommandRunHostRow : ComponentBase
{
	private CommandHostResult? _rendered;
	private bool _renderedCollapsed;

	[Parameter, EditorRequired]
	public CommandHostResult Result { get; set; } = default!;

	[Parameter]
	public bool Collapsed { get; set; }

	[Parameter]
	public EventCallback OnToggle { get; set; }

	private string RowClass => Result.IsProblem ? "mt-run-host mt-run-host--problem" : "mt-run-host";

	private string ExitClass => Result.ExitCode == 0 ? "mt-run-host__exit" : "mt-run-host__exit mt-run-host__exit--bad";

	protected override bool ShouldRender()
	{
		if (ReferenceEquals(_rendered, Result) && _renderedCollapsed == Collapsed)
		{
			return false;
		}

		_rendered = Result;
		_renderedCollapsed = Collapsed;
		return true;
	}

	private static string CutNote(CommandOutputText output) =>
		$"Only the start is kept of {DisplayFormat.Bytes(output.TotalBytes)}.";

	private async Task OnHeadKeyDownAsync(KeyboardEventArgs args)
	{
		if (args.Key is "Enter" or " ")
		{
			await OnToggle.InvokeAsync();
		}
	}
}
