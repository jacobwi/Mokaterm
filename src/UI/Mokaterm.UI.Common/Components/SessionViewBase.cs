using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Common.Components;

/// <summary>Base for module-provided session views, rendered inside a session tab.</summary>
public abstract class SessionViewBase : ComponentBase
{
	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>True while this session's tab is the visible one. Views stay mounted when hidden.</summary>
	[Parameter]
	public bool IsActive { get; set; }
}
