using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// A target typed into quick connect that is to be saved before it opens: the unsaved host and login the connection
/// editor starts from.
/// </summary>
/// <remarks>
/// A class and not a record: both profiles name the machine and the user, which a record would print. Public because
/// <see cref="Shell.ConnectionEditorRequest"/> carries it.
/// </remarks>
public sealed class QuickConnectDraft(HostProfile host, ConnectionProfile connection)
{
	public HostProfile Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

	public ConnectionProfile Connection { get; } = connection ?? throw new ArgumentNullException(nameof(connection));
}
