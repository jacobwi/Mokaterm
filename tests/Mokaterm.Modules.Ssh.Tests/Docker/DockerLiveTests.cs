using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// Reads, and only reads, the Docker engine on the server named by <c>MOKATERM_TEST_SSH</c>: detection and every listing
/// the manager shows, parsed for real. Nothing is started, stopped, pulled or removed. Skipped when the variable is not
/// set, and when that server has no Docker the login may use.
/// </summary>
public sealed class DockerLiveTests
{
	private const string ServerVariable = "MOKATERM_TEST_SSH";

	[Fact]
	public async Task Docker_OnTheTestServer_IsDetectedAndListed()
	{
		string value = LiveEndpoint.Require(ServerVariable, $"Set {ServerVariable}=user:password@host:port to read Docker on a real server.");
		LiveEndpoint server = LiveEndpoint.Parse(value, SshProtocolProvider.SshDescriptor.DefaultPort);
		(string user, string password) = server.RequireLogin(ServerVariable);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromMinutes(3));
		CancellationToken token = timeout.Token;

		HostProfile host = new() { Id = Guid.NewGuid(), Address = server.Host };
		ProtocolConnectContext context = new()
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
			Purpose = ConnectPurpose.Commands,
		};

		IProtocolSession session = await new SshProtocolProvider(new SshConnector(new FakeSettingsService())).ConnectAsync(context, token);
		await using (session)
		{
			IRemoteCommandFeature commands = session.GetFeature<IRemoteCommandFeature>() ?? throw new InvalidOperationException("No commands.");
			using DockerSessionFeature docker = DockerSessionFeature.ForCommands(commands, TimeProvider.System, server.Host, user);

			DockerEngineStatus status = await docker.DetectAsync(token);
			if (!status.IsReady)
			{
				Assert.False(string.IsNullOrWhiteSpace(status.Message));
				Assert.Skip($"Docker on the test server is {status.Availability}: {status.Message}");
				return;
			}

			Assert.True(status.Version!.HasServer);
			DockerClient client = docker.Client;
			IReadOnlyList<DockerContainer> containers = await client.ListContainersAsync(token);
			Assert.All(containers, container => Assert.Null(Abstractions.Containers.DockerNames.ContainerProblem(container.Name)));
			Assert.All(await client.ListImagesAsync(token), image => Assert.False(string.IsNullOrEmpty(image.Id)));
			Assert.All(await client.ListVolumesAsync(token), volume => Assert.False(string.IsNullOrEmpty(volume.Name)));
			Assert.Contains(await client.ListNetworksAsync(containers, token), network => network.IsBuiltIn);
			Assert.NotEmpty(await client.DiskUsageAsync(token));
			foreach (ComposeProject project in await client.ListComposeProjectsAsync(docker.Compose, containers, token))
			{
				Assert.All(project.ConfigFiles, file => Assert.StartsWith("/", file, StringComparison.Ordinal));
			}

			if (containers.FirstOrDefault(container => container.IsRunning) is { } running)
			{
				DockerContainerDetails details = await client.InspectAsync(running.Name, token);
				Assert.Equal(running.Name, details.Name);
				Assert.DoesNotContain(DockerInspectParser.Mask, DockerInspectParser.RawJson(details.Raw, revealEnvironment: true), StringComparison.Ordinal);
			}
		}
	}
}
