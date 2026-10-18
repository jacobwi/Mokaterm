using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// What a <see cref="ISystemNotifications"/> notification says, and the ids a click on it comes back with. The system
/// keeps notifications in a store of its own after the app has gone, so what goes in here is kept to what the user needs
/// to recognise the event, and the ids are the only thing handed back to the app.
/// </summary>
public sealed record SystemNotification
{
	/// <summary>The first line, in bold.</summary>
	public required string Title { get; init; }

	public required string Message { get; init; }

	/// <summary>A third line, or null for none.</summary>
	public string? Detail { get; init; }

	/// <summary>The session a click selects.</summary>
	public Guid? SessionId { get; init; }

	/// <summary>
	/// The saved login behind that session, for a click that arrives after the session was closed and opened again under a
	/// new id. Null for a session that was never saved.
	/// </summary>
	public Guid? ConnectionId { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// The title names a machine, and the detail can be a server's line holding a token: a log gets neither.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"Title = {SecretText.Describe(Title)}, Message = {SecretText.Describe(Message)}, Detail = {SecretText.Describe(Detail)}, SessionId = {SessionId}, ConnectionId = {ConnectionId}");
		return true;
	}
}
