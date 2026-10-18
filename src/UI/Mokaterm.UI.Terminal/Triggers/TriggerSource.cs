using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>The session a trigger matched in, as far as a notice about it needs to know.</summary>
/// <param name="SessionId">The session's tab.</param>
/// <param name="ConnectionId">The saved login behind it, or null for a quick connection that was never saved.</param>
/// <param name="Label">What the session is called on its tab.</param>
internal sealed record TriggerSource(Guid SessionId, Guid? ConnectionId, string? Label)
{
	public static TriggerSource From(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return new TriggerSource(session.Id, session.IsTransient ? null : session.Connection.Id, session.Title);
	}

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// A session's title names its machine and its user.
		builder.Append(CultureInfo.InvariantCulture, $"SessionId = {SessionId}, ConnectionId = {ConnectionId}, Label = {SecretText.Describe(Label)}");
		return true;
	}
}
