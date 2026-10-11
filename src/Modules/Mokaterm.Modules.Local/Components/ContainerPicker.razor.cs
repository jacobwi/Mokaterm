using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Local.Containers;

namespace Mokaterm.Modules.Local.Components;

/// <summary>
/// A name field with a "Choose" button beside it that lists what the field can name, from the tool that knows: docker
/// for containers, kubectl for pods. The list runs in the background, can be cancelled, and goes away once something is
/// picked; the field stays the place a name is typed.
/// </summary>
public sealed partial class ContainerPicker : IDisposable
{
	private CancellationTokenSource? _loadCancellation;
	private ContainerChoiceList? _list;
	private bool _loading;

	/// <summary>Lists the choices. Called again for every "Choose", so the list is never older than the click.</summary>
	[Parameter, EditorRequired]
	public Func<CancellationToken, Task<ContainerChoiceList>> Load { get; set; } = static _ => Task.FromResult(new ContainerChoiceList());

	[Parameter]
	public EventCallback<ContainerChoice> OnPick { get; set; }

	/// <summary>The field the picked name goes into.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>What is listed, for the list's name and the empty text: <c>containers</c>, <c>pods</c>.</summary>
	[Parameter]
	public string What { get; set; } = "containers";

	/// <summary>The name the login has now, marked in the list.</summary>
	[Parameter]
	public string? Selected { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	private string Empty => $"No {What} were listed.";

	private string ChooseTitle => $"List the {What} to pick from";

	public void Dispose() => Cancel();

	private bool IsSelected(ContainerChoice choice) => string.Equals(choice.Name, Selected?.Trim(), StringComparison.Ordinal);

	private async Task LoadAsync()
	{
		Cancel();
		CancellationTokenSource cancellation = new();
		_loadCancellation = cancellation;
		_loading = true;
		_list = null;
		try
		{
			ContainerChoiceList list = await Load(cancellation.Token);
			if (!cancellation.IsCancellationRequested)
			{
				_list = list;
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			// Cancelled from here, or the editor went away.
		}
		finally
		{
			if (ReferenceEquals(_loadCancellation, cancellation))
			{
				_loading = false;
				_loadCancellation = null;
			}

			cancellation.Dispose();
		}
	}

	private void Cancel()
	{
		if (_loadCancellation is { } cancellation)
		{
			_loadCancellation = null;
			_loading = false;
			cancellation.Cancel();
		}
	}

	private async Task PickAsync(ContainerChoice choice)
	{
		_list = null;
		await OnPick.InvokeAsync(choice);
	}
}
