using Microsoft.AspNetCore.Components;
using Moka.Red.Primitives.ChainCheck;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Workspace;

/// <summary>The steps of a session's connect attempt as a chain, with the failure under the step that broke.</summary>
public sealed partial class ConnectChain : ComponentBase
{
	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>The steps to draw, read once by the caller so the chain and the view around it agree.</summary>
	[Parameter, EditorRequired]
	public ConnectStepChain Chain { get; set; } = ConnectStepChain.Empty;

	/// <summary>What puts a failure right, drawn at the end of the failed step's row.</summary>
	[Parameter]
	public RenderFragment? Fix { get; set; }

	private IReadOnlyList<MokaChainStep> Steps => ConnectChainDisplay.Steps(
		Chain,
		Session.State == SessionState.Failed,
		Session.Failure,
		Session.StatusMessage,
		Contributions.GetProtocolIcon(Session.Protocol.Id),
		Fix);
}
