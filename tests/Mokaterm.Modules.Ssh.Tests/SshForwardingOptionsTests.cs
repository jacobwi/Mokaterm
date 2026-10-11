using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Forwarding;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SshForwardingOptionsTests
{
	[Fact]
	public void RoundTrip_KeepsForwardingInTheConnectionOptions()
	{
		SshConnectionOptions options = new()
		{
			StartupCommand = "htop",
			Forwarding = new SshForwardingOptions { ForwardAgent = true, ForwardX11 = true, Display = "localhost:1.0" },
		};

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("true", stored["ssh.forwardAgent"]);
		Assert.Equal("true", stored["ssh.forwardX11"]);
		Assert.Equal("localhost:1.0", stored["ssh.x11Display"]);
		Assert.Equal(options, SshConnectionOptions.FromOptions(stored));
		Assert.Equal(options.GetHashCode(), SshConnectionOptions.FromOptions(stored).GetHashCode());
	}

	[Fact]
	public void Off_IsTheDefault_AndWritesNoKeys()
	{
		ProtocolOptions stored = new SshConnectionOptions().ApplyTo(ProtocolOptions.Empty);

		Assert.Empty(stored.Keys);
		Assert.Equal(SshForwardingOptions.None, SshConnectionOptions.FromOptions(ProtocolOptions.Empty).Forwarding);
	}

	[Fact]
	public void TurningOff_RemovesTheKeys_ButKeepsTheDisplay()
	{
		ProtocolOptions existing = new SshForwardingOptions { ForwardAgent = true, ForwardX11 = true, Display = " :2 " }.ApplyTo(ProtocolOptions.Empty);

		ProtocolOptions stored = new SshForwardingOptions { Display = ":2" }.ApplyTo(existing);

		Assert.Equal(["ssh.x11Display"], stored.Keys);
		Assert.Equal(":2", stored["ssh.x11Display"]);
	}

	[Fact]
	public void From_ReadsWhatItCan()
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With("ssh.forwardAgent", "yes")
			.With("ssh.forwardX11", "True")
			.With("ssh.x11Display", "  ");

		SshForwardingOptions forwarding = SshForwardingOptions.From(options);

		Assert.False(forwarding.ForwardAgent);
		Assert.True(forwarding.ForwardX11);
		Assert.Null(forwarding.Display);
	}

	[Fact]
	public void Equality_SeesForwarding()
	{
		SshConnectionOptions plain = new();
		SshConnectionOptions forwarding = new() { Forwarding = new SshForwardingOptions { ForwardAgent = true } };

		Assert.NotEqual(plain, forwarding);
	}
}
