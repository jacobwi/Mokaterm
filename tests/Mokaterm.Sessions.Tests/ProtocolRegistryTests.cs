using Mokaterm.Abstractions.Protocols;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class ProtocolRegistryTests
{
	[Fact]
	public void Protocols_AreOrderedByOrderThenDisplayName()
	{
		ProtocolRegistry registry = new(
		[
			new FakeProtocolProvider(TestProtocols.Ftp with { Order = 2 }),
			new FakeProtocolProvider(TestProtocols.Ssh with { Order = 1 }),
			new FakeProtocolProvider(TestProtocols.Sftp with { Order = 1 }),
			new FakeProtocolProvider(Descriptor("telnet", "Telnet", order: 0)),
		]);

		string[] expected = ["telnet", "sftp", "ssh", "ftp"];
		Assert.Equal(expected, registry.Protocols.Select(protocol => protocol.Id));
	}

	[Fact]
	public void Constructor_DuplicateIdInAnyCase_FailsWithClearMessage()
	{
		FakeProtocolProvider first = new(TestProtocols.Ssh);
		FakeProtocolProvider second = new(TestProtocols.Ssh with { Id = "SSH" });

		InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ProtocolRegistry([first, second]));

		Assert.Contains("'SSH'", error.Message, StringComparison.Ordinal);
		Assert.Contains("registered twice", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void FindAndGet_IgnoreCase_AndGetThrowsForUnknownIds()
	{
		FakeProtocolProvider ssh = new(TestProtocols.Ssh);
		ProtocolRegistry registry = new([ssh, new FakeProtocolProvider(TestProtocols.Ftp)]);

		Assert.Same(ssh, registry.Find("SSH"));
		Assert.Same(ssh, registry.Get("Ssh"));
		Assert.Null(registry.Find("rdp"));
		Assert.Throws<KeyNotFoundException>(() => registry.Get("rdp"));
	}

	private static ProtocolDescriptor Descriptor(string id, string displayName, int order) => new()
	{
		Id = id,
		DisplayName = displayName,
		DefaultPort = 23,
		Capabilities = ProtocolCapabilities.Terminal,
		Order = order,
	};
}
