using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Builds the <see cref="ProtocolConnectContext"/> a provider is handed, over the host and login profiles a test has no
/// opinion about. The module harnesses fill in what their own provider reads.
/// </summary>
internal sealed class ConnectContext
{
	public required string ProtocolId { get; init; }

	public required int Port { get; init; }

	public required ICredentialSource Credentials { get; init; }

	/// <summary>Whatever the protocol identifies a host by, or a <see cref="RefusingHostVerifier"/> when it has none.</summary>
	public required IHostIdentityVerifier Verifier { get; init; }

	public string Address { get; init; } = "127.0.0.1";

	public ProtocolOptions Options { get; init; } = ProtocolOptions.Empty;

	/// <summary>Prompts, which a provider under test should not be raising at all.</summary>
	public IUserInteraction Interaction { get; init; } = new ThrowingInteraction();

	/// <summary>
	/// A real sink by default, so every provider's progress reporting runs in every test rather than only where a test
	/// asked for the messages. <see cref="WithoutStatus"/> covers the caller that has none.
	/// </summary>
	public IProgress<string>? Status { get; init; }

	/// <summary>Leaves the context without a status sink, the way the shell does when nothing is watching.</summary>
	public bool WithoutStatus { get; init; }

	/// <summary>The opening terminal size, for a protocol that has a terminal.</summary>
	public TerminalSize? TerminalSize { get; init; }

	public ProtocolConnectContext Build()
	{
		HostProfile host = new() { Id = Guid.NewGuid(), Address = Address };
		return new ProtocolConnectContext
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = ProtocolId,
				Port = Port,
				Username = Credentials.Username,
				AuthenticationMethod = Credentials.Method,
				Options = Options,
			},
			Port = Port,
			Credentials = Credentials,
			HostVerifier = Verifier,
			Interaction = Interaction,
			Status = WithoutStatus ? null : Status ?? new RecordingProgress<string>(),
			TerminalSize = TerminalSize ?? Abstractions.Terminal.TerminalSize.Default,
		};
	}
}
