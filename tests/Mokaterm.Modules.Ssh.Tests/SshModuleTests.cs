using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Components;
using Mokaterm.Modules.Ssh.Keys;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SshModuleTests
{
	[Fact]
	public void Info_DescribesTheModule()
	{
		ModuleInfo info = new SshModule().Info;

		Assert.Equal("ssh", info.Id);
		Assert.Equal("SSH & SFTP", info.Name);
		Assert.False(string.IsNullOrWhiteSpace(info.Version));
		Assert.DoesNotContain('+', info.Version);
	}

	[Fact]
	public void AddSsh_AddsTheModuleThroughTheBuilder()
	{
		RecordingBuilder builder = new();

		IMokatermBuilder returned = builder.AddSsh();

		Assert.Same(builder, returned);
		Assert.Equal("ssh", Assert.Single(builder.Modules).Info.Id);
	}

	[Fact]
	public void Providers_ResolveWithTheirDescriptors()
	{
		using ServiceProvider services = BuildServices();

		IProtocolProvider[] providers = [.. services.GetServices<IProtocolProvider>()];

		Assert.Equal(2, providers.Length);
		ProtocolDescriptor ssh = Assert.Single(providers, provider => provider.Descriptor.Id == "ssh").Descriptor;
		ProtocolDescriptor sftp = Assert.Single(providers, provider => provider.Descriptor.Id == "sftp").Descriptor;

		AuthenticationMethod[] methods =
		[
			AuthenticationMethod.Password,
			AuthenticationMethod.PublicKey,
			AuthenticationMethod.KeyboardInteractive,
			AuthenticationMethod.Agent,
		];

		Assert.Equal("SSH", ssh.DisplayName);
		Assert.Equal(22, ssh.DefaultPort);
		Assert.Equal(ProtocolCapabilities.Terminal | ProtocolCapabilities.FileSystem | ProtocolCapabilities.Elevation, ssh.Capabilities);
		Assert.Equal(methods, ssh.AuthenticationMethods);
		Assert.Null(ssh.VariantOf);
		Assert.Equal(0, ssh.Order);

		Assert.Equal("SFTP", sftp.DisplayName);
		Assert.Equal(22, sftp.DefaultPort);
		Assert.Equal(ProtocolCapabilities.FileSystem | ProtocolCapabilities.Elevation, sftp.Capabilities);
		Assert.Equal(methods, sftp.AuthenticationMethods);
		Assert.Equal("ssh", sftp.VariantOf);
		Assert.Equal(1, sftp.Order);
	}

	[Fact]
	public void ConfigureServices_TwiceStillRegistersEachProviderOnce()
	{
		ServiceCollection collection = new();
		collection.AddSingleton<ISettingsService>(new FakeSettingsService());
		new SshModule().ConfigureServices(collection);
		new SshModule().ConfigureServices(collection);
		using ServiceProvider services = collection.BuildServiceProvider();

		Assert.Equal(2, services.GetServices<IProtocolProvider>().Count());
		Assert.IsType<SshPrivateKeyInspector>(services.GetRequiredService<IPrivateKeyInspector>());
	}

	[Fact]
	public void UiContributions_RegisterEditorsIconsAndSettingsPage()
	{
		using ServiceProvider services = BuildServices();
		IUiContributions contributions = services.GetRequiredService<IUiContributions>();

		ProtocolUiDescriptor ssh = contributions.FindProtocol("ssh") ?? throw new InvalidOperationException("ssh UI missing");
		ProtocolUiDescriptor sftp = contributions.FindProtocol("sftp") ?? throw new InvalidOperationException("sftp UI missing");
		Assert.Equal(typeof(SshOptionsEditor), ssh.OptionsEditor);
		Assert.Equal(typeof(SshOptionsEditor), sftp.OptionsEditor);
		Assert.Equal(MokatermIcons.Terminal.Name, ssh.Icon.Name);
		Assert.Equal(MokatermIcons.FolderOpen.Name, sftp.Icon.Name);

		SessionToolDescriptor tool = Assert.Single(contributions.FindSessionTools("ssh"));
		Assert.Equal("Port forwarding", tool.Title);
		Assert.Equal(typeof(SshTunnelsView), tool.Component);
		Assert.False(tool.ShowWhileDisconnected);

		// Nothing is inherited: an sftp session has no SSH client of its own to forward ports through.
		Assert.Empty(contributions.FindSessionTools("sftp"));

		SettingsPageDescriptor page = Assert.Single(contributions.SettingsPages, candidate => candidate.Id == "ssh");
		Assert.Equal("SSH", page.Title);
		Assert.Equal(SettingsPageGroup.Protocols, page.Group);
		Assert.Equal(typeof(SshSettingsPage), page.Component);
		Assert.Equal("keepalive timeout sudo sftp agent tunnel forward", page.Keywords);
		Assert.Equal(MokatermIcons.Terminal.Name, page.Icon.Name);
	}

	[Fact]
	public void Settings_DefaultsMatchTheSpecification()
	{
		SshSettings settings = new();

		Assert.Equal("ssh", SshSettings.SectionKey);
		Assert.Equal(30, settings.KeepAliveSeconds);
		Assert.Equal(15, settings.ConnectTimeoutSeconds);
		Assert.Equal(3, settings.AuthenticationAttempts);
		Assert.Equal("sudo", settings.SudoCommand);
		Assert.Equal("/tmp", settings.StagingDirectory);
		Assert.Equal(120, settings.PromptTimeoutSeconds);
		Assert.Equal("", settings.AgentEndpoint);
	}

	private static ServiceProvider BuildServices()
	{
		ServiceCollection collection = new();
		collection.AddSingleton<ISettingsService>(new FakeSettingsService());
		new SshModule().ConfigureServices(collection);
		return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
	}

	private sealed class RecordingBuilder : IMokatermBuilder
	{
		public IServiceCollection Services { get; } = new ServiceCollection();

		public List<IMokatermModule> Modules { get; } = [];

		public IMokatermBuilder AddModule(IMokatermModule module)
		{
			Modules.Add(module);
			return this;
		}
	}
}
