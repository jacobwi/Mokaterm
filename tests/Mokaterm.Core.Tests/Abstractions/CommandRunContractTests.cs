using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>The rules a command run shares between the runner, the dialog and the verifier.</summary>
public sealed class CommandRunContractTests
{
	private static readonly HostIdentity SshKey = new()
	{
		Host = "10.0.0.5",
		Port = 22,
		Kind = HostIdentityKind.SshHostKey,
		Algorithm = "ssh-ed25519",
		Fingerprint = "SHA256:abc",
	};

	[Theory]
	[InlineData(HostIdentityMatch.Trusted, HostKeyPolicy.Ask, HostTrustAction.Accept)]
	[InlineData(HostIdentityMatch.Trusted, HostKeyPolicy.Strict, HostTrustAction.Accept)]
	[InlineData(HostIdentityMatch.Unknown, HostKeyPolicy.Ask, HostTrustAction.Ask)]
	[InlineData(HostIdentityMatch.Unknown, HostKeyPolicy.AcceptNew, HostTrustAction.TrustNew)]
	[InlineData(HostIdentityMatch.Unknown, HostKeyPolicy.Strict, HostTrustAction.Refuse)]
	[InlineData(HostIdentityMatch.Changed, HostKeyPolicy.Ask, HostTrustAction.Ask)]
	[InlineData(HostIdentityMatch.Changed, HostKeyPolicy.AcceptNew, HostTrustAction.Refuse)]
	[InlineData(HostIdentityMatch.Changed, HostKeyPolicy.Strict, HostTrustAction.Refuse)]
	public void HostTrustRule_Decide_FollowsThePolicy(HostIdentityMatch match, HostKeyPolicy policy, HostTrustAction expected) =>
		Assert.Equal(expected, HostTrustRule.Decide(SshKey, match, policy));

	[Fact]
	public void HostTrustRule_Decide_AcceptsANewCertificateTheSystemTrusts_ButNotAChangedOne()
	{
		HostIdentity certificate = SshKey with { Kind = HostIdentityKind.TlsCertificate, ChainTrusted = true };

		Assert.Equal(HostTrustAction.Accept, HostTrustRule.Decide(certificate, HostIdentityMatch.Unknown, HostKeyPolicy.Strict));
		Assert.Equal(HostTrustAction.Ask, HostTrustRule.Decide(certificate, HostIdentityMatch.Changed, HostKeyPolicy.Ask));
	}

	[Theory]
	[InlineData("uptime", null)]
	[InlineData("  df -h  ", null)]
	[InlineData("for i in 1 2; do\n\techo $i\ndone", null)]
	[InlineData("", "Type a command to run.")]
	[InlineData("   \n ", "Type a command to run.")]
	public void RemoteCommandRequest_Problem_AllowsScriptsAndRefusesBlankText(string command, string? expected) =>
		Assert.Equal(expected, RemoteCommandRequest.Problem(command));

	[Fact]
	public void RemoteCommandRequest_Problem_RefusesControlCharactersOtherThanTabsAndLineBreaks()
	{
		Assert.NotNull(RemoteCommandRequest.Problem("echo " + (char)0));
		Assert.NotNull(RemoteCommandRequest.Problem("printf " + (char)0x1b + "[2J"));
		Assert.Equal("uptime", RemoteCommandRequest.Normalize("\tuptime \r\n"));
	}

	[Fact]
	public void CommandRunSettings_Clamped_KeepsEveryNumberInItsRange()
	{
		CommandRunSettings wild = new() { ParallelHosts = 500, TimeoutSeconds = 0, OutputKilobytes = -3 };

		CommandRunSettings clamped = wild.Clamped();

		Assert.Equal(CommandRunSettings.MaxParallelHosts, clamped.ParallelHosts);
		Assert.Equal(CommandRunSettings.MinTimeoutSeconds, clamped.TimeoutSeconds);
		Assert.Equal(CommandRunSettings.MinOutputKilobytes, clamped.OutputKilobytes);
		Assert.Equal(new CommandRunSettings(), new CommandRunSettings().Clamped());
		Assert.True(new CommandRunSettings().SkipHostsThatAsk);
		Assert.True(CommandRunSettings.MinOutputKilobytes * 1024 >= RemoteCommandRequest.MinOutputLimitBytes);
	}

	[Fact]
	public void CommandRunEligibility_OnlyProtocolsThatRunCommands()
	{
		ProtocolDescriptor ssh = new() { Id = "ssh", DisplayName = "SSH", DefaultPort = 22, Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands };
		ProtocolDescriptor telnet = new() { Id = "telnet", DisplayName = "Telnet", DefaultPort = 23, Capabilities = ProtocolCapabilities.Terminal };

		Assert.Null(CommandRunEligibility.WhyNot(ssh));
		Assert.Equal("Telnet logins cannot run a command without a terminal.", CommandRunEligibility.WhyNot(telnet));
		Assert.NotNull(CommandRunEligibility.WhyNot(null));
	}

	[Fact]
	public void Records_ThatCarryACommandOrItsOutput_NeverPrintThem()
	{
		const string Secret = "Authorization: Bearer s3cr3t";
		CapturedOutput output = new(System.Text.Encoding.UTF8.GetBytes(Secret), 28);

		Assert.DoesNotContain(Secret, new RemoteCommandRequest { Command = Secret }.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain(Secret, new CommandRunRequest { Command = Secret, ConnectionIds = [Guid.NewGuid()] }.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("s3cr3t", output.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("s3cr3t", new CommandOutputText(Secret, 28, false).ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain(
			"s3cr3t",
			new RemoteCommandOutcome { ExitCode = 0, Output = output, Error = CapturedOutput.Empty }.ToString(),
			StringComparison.Ordinal);
	}
}
