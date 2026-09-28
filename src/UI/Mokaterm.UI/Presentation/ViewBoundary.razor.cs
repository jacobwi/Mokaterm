using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// Keeps an exception inside the view it came from. Without one, an exception in any component ends the Blazor Server
/// circuit with every session in it, and takes the desktop app down; with one, the view says what went wrong and can
/// be loaded again.
/// </summary>
public sealed partial class ViewBoundary : ErrorBoundaryBase
{
	[Inject]
	private ILogger<ViewBoundary> Logger { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	/// <summary>What failed, as the error title reads it: "{Name} stopped working".</summary>
	[Parameter]
	public string Name { get; set; } = "This view";

	/// <summary>A sentence under the error, such as what still works.</summary>
	[Parameter]
	public string? Hint { get; set; }

	/// <summary>Buttons next to "Try again", such as closing the tab that failed.</summary>
	[Parameter]
	public RenderFragment? Actions { get; set; }

	/// <summary>Centers the error in the space the view filled, for panels and whole screens.</summary>
	[Parameter]
	public bool Fill { get; set; }

	/// <summary>
	/// Reports the error as a notice and renders the content again straight away. For overlays such as menus and the
	/// command palette, which fail in the actions they run rather than in what they show. The renderer still recreates
	/// the content, and <see cref="ErrorBoundaryBase.MaximumErrorCount"/> still ends a loop of failures.
	/// </summary>
	[Parameter]
	public bool NotifyOnly { get; set; }

	/// <summary>
	/// Runs once a failure is caught, before the content is recreated. For state the failed content owned outside
	/// itself, such as a service dialog whose caller would otherwise wait for an answer forever.
	/// </summary>
	[Parameter]
	public Action? OnFailed { get; set; }

	private string Title => $"{Name} stopped working";

	/// <summary>The first line of the message, or the exception type when there is none. JS errors carry their stack in it.</summary>
	internal static string Describe(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string message = exception.Message;
		int end = message.AsSpan().IndexOfAny('\r', '\n');
		string line = (end < 0 ? message : message[..end]).Trim();
		return line.Length > 0 ? line : exception.GetType().Name;
	}

	protected override Task OnErrorAsync(Exception exception)
	{
		Logger.LogError(exception, "{View} failed; the error was kept inside it.", Name);
		OnFailed?.Invoke();
		if (NotifyOnly)
		{
			Interaction.Notify(NoticeSeverity.Error, Describe(exception), Title);
		}

		return Task.CompletedTask;
	}
}
