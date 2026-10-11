using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Accounts;
using Mokaterm.Modules.Ssh.Admin.Cron;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.SystemLogs;
using Mokaterm.Modules.Ssh.Admin.Network;
using Mokaterm.Modules.Ssh.Admin.Overview;
using Mokaterm.Modules.Ssh.Admin.Processes;
using Mokaterm.Modules.Ssh.Admin.Storage;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// The system view's reads against a real server named by <c>MOKATERM_TEST_SSH</c>. Read only: nothing here runs as
/// root or changes the machine. Skipped when the variable is not set.
/// </summary>
public sealed class SystemAdminIntegrationTests
{
	private const string ServerVariable = "MOKATERM_TEST_SSH";

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Ssh_DetectsTheServer_AndEveryReadParses()
	{
		string value = LiveEndpoint.Require(ServerVariable, $"Set {ServerVariable}=user:password@host:port to run tests against a real SSH server.");
		LiveEndpoint server = LiveEndpoint.Parse(value, SshProtocolProvider.SshDescriptor.DefaultPort);
		CancellationToken token = TestContext.Current.CancellationToken;

		IProtocolSession session = await new SshProtocolProvider(new SshConnector(new FakeSettingsService())).ConnectAsync(Context(server), token);
		await using (session)
		{
			SystemAdminFeature feature = session.GetFeature<SystemAdminFeature>() ?? throw new InvalidOperationException("No system view.");
			SystemProfile profile = await feature.DetectAsync(refresh: false, token);
			Assert.NotEmpty(profile.Facts.KernelName);
			Assert.NotNull(profile.Facts.Uid);
			if (profile.UnsupportedReason is not null)
			{
				return;
			}

			SystemCapabilities capabilities = profile.Capabilities;
			OverviewSnapshot overview = OverviewCommands.Parse((await feature.ReadAsync(OverviewCommands.Read(), token)).Output);
			Assert.NotNull(overview.Uptime);
			Assert.NotNull(overview.MemoryTotalKb);

			if (capabilities.Services is { } services)
			{
				Assert.NotEmpty(services.ParseList((await feature.ReadAsync(services.List(), token)).Output));
			}

			if (capabilities.Packages is { } packages)
			{
				Assert.NotEmpty(packages.ParseInstalled((await feature.ReadAsync(packages.ListInstalled(), token)).Output));
			}

			AccountsSnapshot accounts = AccountCommands.Parse((await feature.ReadAsync(AccountCommands.Read(asRoot: false), token)).Output);
			Assert.Contains(accounts.Users, user => user.Name == "root");

			if (capabilities.HasNetworkTools)
			{
				Assert.NotEmpty(NetworkParser.Parse((await feature.ReadAsync(NetworkCommands.Read(asRoot: false), token)).Output).Interfaces);
			}

			Assert.NotEmpty(DiskCommands.Parse((await feature.ReadAsync(DiskCommands.Read(), token)).Output).Mounts);
			if (capabilities.HasProcessTools)
			{
				Assert.NotEmpty(ProcessCommands.Parse((await feature.ReadAsync(ProcessCommands.List(asRoot: false), token)).Output));
			}

			Assert.NotEmpty(capabilities.Clock.Read().Body);
			_ = capabilities.HostName.Read();
			if (capabilities.HasCrontab)
			{
				Assert.NotNull(CronCommands.ReadResult(await feature.ReadAsync(CronCommands.Read(asRoot: false), token), out _));
			}

			if (capabilities.HasTimers)
			{
				_ = CronCommands.ParseTimers((await feature.ReadAsync(CronCommands.ListTimers(), token)).Output);
			}

			AdminOutcome log = await feature.ReadAsync(capabilities.HasJournal ? LogCommands.Journal(null, null, 20, asRoot: false) : LogCommands.ListFiles(asRoot: false), token);
			Assert.NotNull(log.Output);
		}
	}

	private static ProtocolConnectContext Context(LiveEndpoint server)
	{
		(string user, string password) = server.RequireLogin(ServerVariable);
		HostProfile host = new() { Id = Guid.NewGuid(), Address = server.Host };
		return new ProtocolConnectContext
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = SshProtocolIds.Ssh,
				Port = server.Port,
				Username = user,
				AuthenticationMethod = AuthenticationMethod.Password,
			},
			Port = server.Port,
			Credentials = new PasswordCredentialSource(user, password),
			HostVerifier = FakeHostVerifier.Accepting(),
			Interaction = new DismissingInteraction(),
			TerminalSize = new TerminalSize(80, 24),
		};
	}
}
