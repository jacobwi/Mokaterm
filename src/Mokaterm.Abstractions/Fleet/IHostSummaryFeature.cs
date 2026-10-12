using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Abstractions.Fleet;

/// <summary>
/// Session feature for protocols that can say what their machine runs and how it is doing, reached through
/// <see cref="Protocols.IProtocolSession.GetFeature{TFeature}"/> on a session opened for
/// <see cref="Protocols.ConnectPurpose.Commands"/>, and offered only by protocols that declare
/// <see cref="Protocols.ProtocolCapabilities.HostSummary"/>.
/// </summary>
/// <remarks>
/// A read only ever reads: it changes nothing on the machine and runs nothing slow on its behalf, such as fetching the
/// package lists. Its time and its output are bounded by the feature, and the caller's token ends it sooner.
/// </remarks>
public interface IHostSummaryFeature
{
	/// <summary>Reads the machine. Parts it cannot read stay empty in the summary rather than failing the whole read.</summary>
	/// <exception cref="RemoteCommandException">The read could not run at all, or the connection dropped while it ran.</exception>
	Task<HostSummary> ReadSummaryAsync(CancellationToken cancellationToken);
}
