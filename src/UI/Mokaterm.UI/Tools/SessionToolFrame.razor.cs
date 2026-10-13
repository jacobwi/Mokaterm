using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tools;

/// <summary>
/// A session tool, such as Docker, in its dialog over the session or in a window of its own. Where it opens, moves and
/// closes is <see cref="SessionToolActions"/>' business; this only wires the frame's buttons to it.
/// </summary>
public sealed partial class SessionToolFrame : ComponentBase
{
	[Parameter, EditorRequired]
	public ToolPresentation Presentation { get; set; }

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	[Parameter, EditorRequired]
	public SessionToolDescriptor Tool { get; set; } = default!;

	/// <summary>Whether the session's tab is the one on screen. A tool in its own window is always on screen.</summary>
	[Parameter]
	public bool IsActive { get; set; } = true;

	[Inject]
	private SessionToolActions Tools { get; set; } = default!;

	private Dictionary<string, object> Parameters => new()
	{
		[nameof(SessionViewBase.Session)] = Session,
		[nameof(SessionViewBase.IsActive)] = Presentation == ToolPresentation.Window || IsActive,
	};

	// A dialog opens only while the tool can show, so while its session is down there is no way back to offer.
	private EventCallback ReturnCallback => Tools.Available(Session).Contains(Tool)
		? EventCallback.Factory.Create(this, () => Tools.ReturnToDialog(Session, Tool))
		: default;

	private Task PopOutAsync() => Tools.PopOutAsync(Session, Tool);

	private void CloseDialog() => Tools.CloseDialog(Session.Id);
}
