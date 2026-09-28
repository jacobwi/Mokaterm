namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// A live connection. Capabilities are exposed as features so new protocols can add their own
/// without changing this interface. Disposing closes the connection.
/// </summary>
public interface IProtocolSession : IAsyncDisposable
{
	/// <summary>
	/// Completes when the connection ends. Runs to completion on a clean close and faults with the cause
	/// when the connection drops.
	/// </summary>
	Task Completion { get; }

	/// <summary>
	/// Returns a feature such as <see cref="Terminal.ITerminalChannel"/> or
	/// <see cref="FileSystem.IFileSystemFeature"/>, or null when the session does not support it.
	/// </summary>
	TFeature? GetFeature<TFeature>() where TFeature : class;
}
