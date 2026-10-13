using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Workspace;

/// <summary>A tool's session and its state, which follows the session as it drops, reconnects and connects again.</summary>
public sealed partial class SessionToolSubject : ComponentBase, IDisposable
{
	private ISessionHandle? _watched;

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	// The session's own words: the countdown to the next attempt while it reconnects, why it ended after a drop.
	private string? StateTitle => Session.State == SessionState.Connected ? null : Session.StatusMessage;

	public void Dispose() => Watch(null);

	protected override void OnParametersSet() => Watch(Session);

	private void Watch(ISessionHandle? session)
	{
		if (ReferenceEquals(_watched, session))
		{
			return;
		}

		if (_watched is not null)
		{
			_watched.Changed -= OnSessionChanged;
		}

		_watched = session;
		if (session is not null)
		{
			session.Changed += OnSessionChanged;
		}
	}

	private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);
}
