using System.Security.Cryptography;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.DevHost.Demo;
using Mokaterm.DevHost.Demo.Mqtt;
using Mokaterm.DevHost.Demo.Telnet;
using Mokaterm.DevHost.Demo.Vnc;
using Mokaterm.Modules.Mqtt;
using Mokaterm.Modules.Ssh;
using Mokaterm.Modules.Telnet;

namespace Mokaterm.DevHost.Hosting;

/// <summary>
/// Creates this run's vault with the generated master password and fills it through the real repositories: folders, hosts,
/// demo logins that connect without prompting, a shared password and a private key.
/// </summary>
internal sealed class DevVaultSeeder
{
	private const string FtpProtocolId = "ftp";

	private const string VncProtocolId = "vnc";

	private const string RdpProtocolId = "rdp";

	private const string TelnetProtocolId = "telnet";

	private const string MqttProtocolId = "mqtt";

	/// <summary>Nothing listens here. Writing an RDP server is out of scope, so the workbench only shows the failure path.</summary>
	private const int DeadRdpPort = 13389;

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ISettingsService _settings;
	private readonly DevMasterPassword _masterPassword;
	private readonly DemoVncServer _vnc;
	private readonly DemoTelnetServer _telnet;
	private readonly DemoMqttServer _mqtt;
	private readonly IAppEnvironment _environment;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<DevVaultSeeder> _logger;

	public DevVaultSeeder(
		IServiceScopeFactory scopeFactory,
		ISettingsService settings,
		DevMasterPassword masterPassword,
		DemoVncServer vnc,
		DemoTelnetServer telnet,
		DemoMqttServer mqtt,
		IAppEnvironment environment,
		TimeProvider timeProvider,
		ILogger<DevVaultSeeder> logger)
	{
		_scopeFactory = scopeFactory;
		_settings = settings;
		_masterPassword = masterPassword;
		_vnc = vnc;
		_telnet = telnet;
		_mqtt = mqtt;
		_environment = environment;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public async Task SeedAsync(CancellationToken cancellationToken)
	{
		await _settings.LoadAsync(cancellationToken);

		// Nobody knows the generated password, so an idle lock would strand the page until a reload.
		await _settings.UpdateAsync<SecuritySettings>(security => security with { AutoLockMinutes = 0 }, cancellationToken);
		await _settings.FlushAsync(cancellationToken);

		await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
		IServiceProvider services = scope.ServiceProvider;
		IVault vault = services.GetRequiredService<IVault>();
		await vault.LoadAsync(cancellationToken);
		await vault.CreateAsync(_masterPassword.Value, cancellationToken);

		IConnectionRepository connections = services.GetRequiredService<IConnectionRepository>();
		ICredentialStore credentials = services.GetRequiredService<ICredentialStore>();

		ConnectionFolder lab = new() { Id = Guid.NewGuid(), Name = "Lab" };
		ConnectionFolder production = new() { Id = Guid.NewGuid(), Name = "Production", Order = 1 };
		await connections.SaveFolderAsync(lab, cancellationToken);
		await connections.SaveFolderAsync(production, cancellationToken);

		HostProfile buildServer = new() { Id = Guid.NewGuid(), Name = "Build server", Address = "10.10.2.3", FolderId = lab.Id };
		HostProfile web01 = new()
		{
			Id = Guid.NewGuid(),
			Name = "web01",
			Address = "web01.example.internal",
			FolderId = production.Id,
			Environment = HostEnvironment.Production,
			Color = "#ef5350",
		};
		HostProfile nas = new() { Id = Guid.NewGuid(), Name = "nas", Address = "192.168.1.20", Tags = ["storage", "home"] };
		HostProfile desktop = new()
		{
			Id = Guid.NewGuid(),
			Name = "demo desktop",
			Address = "127.0.0.1",
			FolderId = lab.Id,
			Tags = ["vnc"],
		};
		await connections.SaveHostAsync(buildServer, cancellationToken);
		await connections.SaveHostAsync(web01, cancellationToken);
		await connections.SaveHostAsync(nas, cancellationToken);
		await connections.SaveHostAsync(desktop, cancellationToken);

		CredentialInfo deployPassword = await credentials.SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "deploy password", Kind = CredentialKind.Password, Username = "deploy", IsShared = true },
			new CredentialSecretInput { Password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)) },
			cancellationToken);

		CredentialInfo buildKey = await credentials.SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "build key", Kind = CredentialKind.PrivateKey, Username = "abc", IsShared = true },
			new CredentialSecretInput { PrivateKey = CreatePrivateKey() },
			cancellationToken);

		// The demo VNC server generated this password for this run, so opening its login never asks for one.
		CredentialInfo vncPassword = await credentials.SaveAsync(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "demo desktop password", Kind = CredentialKind.Password, IsShared = false },
			new CredentialSecretInput { Password = _vnc.Password },
			cancellationToken);

		// The demo broker checks this login in its CONNECT, so the explorer opens without a prompt.
		CredentialInfo mqttPassword = await credentials.SaveAsync(
			new CredentialInfo
			{
				Id = Guid.NewGuid(),
				Name = "demo broker login",
				Kind = CredentialKind.Password,
				Username = _mqtt.Username,
				IsShared = false,
			},
			new CredentialSecretInput { Password = _mqtt.Password },
			cancellationToken);

		// Same for the demo telnet console: the login below types this at the server's own prompt.
		CredentialInfo telnetPassword = await credentials.SaveAsync(
			new CredentialInfo
			{
				Id = Guid.NewGuid(),
				Name = "demo console login",
				Kind = CredentialKind.Password,
				Username = _telnet.Username,
				IsShared = false,
			},
			new CredentialSecretInput { Password = _telnet.Password },
			cancellationToken);

		// Saved in this order on purpose: ?sessions=N opens the first N demo logins.
		ConnectionProfile abc = DemoLogin(buildServer, "abc") with { IsFavorite = true };
		ConnectionProfile bcd = DemoLogin(buildServer, "bcd");
		ConnectionProfile abcFiles = new()
		{
			Id = Guid.NewGuid(),
			HostId = buildServer.Id,
			ProtocolId = SshProtocolIds.Sftp,
			Username = "abc",
			AuthenticationMethod = AuthenticationMethod.PublicKey,
			CredentialId = buildKey.Id,
		};
		ConnectionProfile root = DemoLogin(web01, "root") with { IsFavorite = true };
		ConnectionProfile deploy = new()
		{
			Id = Guid.NewGuid(),
			HostId = web01.Id,
			ProtocolId = FtpProtocolId,
			Username = "deploy",
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = deployPassword.Id,
		};
		ConnectionProfile admin = DemoLogin(nas, "admin");
		ConnectionProfile screen = new()
		{
			Id = Guid.NewGuid(),
			HostId = desktop.Id,
			ProtocolId = VncProtocolId,
			Port = _vnc.Port,
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = vncPassword.Id,
			Label = "demo desktop (VNC)",
			IsFavorite = true,
		};
		await connections.SaveConnectionAsync(abc, cancellationToken);
		await connections.SaveConnectionAsync(bcd, cancellationToken);
		await connections.SaveConnectionAsync(abcFiles, cancellationToken);
		await connections.SaveConnectionAsync(root, cancellationToken);
		await connections.SaveConnectionAsync(deploy, cancellationToken);
		ConnectionProfile desktopRdp = new()
		{
			Id = Guid.NewGuid(),
			HostId = desktop.Id,
			ProtocolId = RdpProtocolId,
			Port = DeadRdpPort,
			AuthenticationMethod = AuthenticationMethod.Anonymous,
			Label = "demo desktop (RDP, no server)",
		};
		ConnectionProfile console = new()
		{
			Id = Guid.NewGuid(),
			HostId = desktop.Id,
			ProtocolId = TelnetProtocolId,
			Port = _telnet.Port,
			AuthenticationMethod = AuthenticationMethod.Anonymous,
			Label = "demo console (Telnet)",
		};

		// The same console with the saved login typed for you, which is what the automatic login option does.
		ConnectionProfile consoleAuto = new()
		{
			Id = Guid.NewGuid(),
			HostId = desktop.Id,
			ProtocolId = TelnetProtocolId,
			Port = _telnet.Port,
			Username = _telnet.Username,
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = telnetPassword.Id,
			Label = "demo console (Telnet, types the login)",
			Options = new TelnetConnectionOptions { AutoLogin = true }.ApplyTo(ProtocolOptions.Empty),
		};

		// The broker: subscribed to the demo tree and to everything else, so the explorer opens with topics in it.
		ConnectionProfile broker = new()
		{
			Id = Guid.NewGuid(),
			HostId = desktop.Id,
			ProtocolId = MqttProtocolId,
			Port = _mqtt.Port,
			Username = _mqtt.Username,
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = mqttPassword.Id,
			Label = "demo broker (MQTT)",
			IsFavorite = true,
			Options = new MqttConnectionOptions
			{
				ClientId = "mokaterm-workbench",
				Subscriptions =
				[
					MqttSubscription.Create() with { TopicFilter = "mokaterm/demo/#", QualityOfService = MqttQos.AtLeastOnce },
					MqttSubscription.Create() with { TopicFilter = "#" },
				],
			}.ApplyTo(ProtocolOptions.Empty),
		};

		await connections.SaveConnectionAsync(admin, cancellationToken);
		await connections.SaveConnectionAsync(screen, cancellationToken);
		await connections.SaveConnectionAsync(desktopRdp, cancellationToken);
		await connections.SaveConnectionAsync(console, cancellationToken);
		await connections.SaveConnectionAsync(consoleAuto, cancellationToken);
		await connections.SaveConnectionAsync(broker, cancellationToken);

		DateTimeOffset now = _timeProvider.GetUtcNow();
		await connections.MarkConnectedAsync(root.Id, now.AddMinutes(-12), cancellationToken);
		await connections.MarkConnectedAsync(admin.Id, now.AddDays(-2).AddHours(-3), cancellationToken);

		_logger.LogInformation("Seeded a throwaway vault in {DataDirectory}", _environment.DataDirectory);
	}

	// Anonymous logins need no secret, so connecting to a demo host never prompts.
	private static ConnectionProfile DemoLogin(HostProfile host, string username) => new()
	{
		Id = Guid.NewGuid(),
		HostId = host.Id,
		ProtocolId = DemoProtocolProvider.ProtocolId,
		Username = username,
		AuthenticationMethod = AuthenticationMethod.Anonymous,
	};

	// PKCS#1 PEM is a format OpenSSH and SSH.NET both read, and .NET writes it without extra code.
	private static string CreatePrivateKey()
	{
		using RSA rsa = RSA.Create(3072);
		return rsa.ExportRSAPrivateKeyPem();
	}
}
