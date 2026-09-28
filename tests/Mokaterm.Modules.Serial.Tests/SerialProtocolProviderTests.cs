using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.Modules.Serial.Tests.Fakes;

namespace Mokaterm.Modules.Serial.Tests;

/// <summary>
/// What the provider refuses before it touches a port. Every case here fails the checks, so no test in this class
/// opens anything: the machine running them may well have a device on the other end of COM3.
/// </summary>
public sealed class SerialProtocolProviderTests
{
	private const int TestTimeoutMilliseconds = 30_000;

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Connect_WithoutAPortName_SaysWhatToName()
	{
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Connect("   ", cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("COM3", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Connect_WithANameNoDriverTakes_IsRefusedBeforeTheOpen()
	{
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Connect("COM 3", cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Connect_WithAFrameNoUartCanDo_NamesTheFrame()
	{
		SerialConnectionOptions options = SerialConnectionOptions.Default with
		{
			Line = SerialLineSettings.Default with { DataBits = 8, StopBits = SerialStopBits.OnePointFive },
		};

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Connect("COM3", options, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Contains("115200 8N1.5", failure.Message, StringComparison.Ordinal);
		Assert.Contains("five data bits", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Connect_TakesThePortNameFromTheConnectionOverTheHostAddress()
	{
		// The name in the options is the one that is checked, so a bad one there is what fails.
		SerialConnectionOptions options = SerialConnectionOptions.Default with { PortName = "COM 9" };

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Connect("COM3", options, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Contains("spaces", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Connect_AsksForNoCredentials()
	{
		FakeCredentialSource credentials = new();

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Connect("", credentials: credentials, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(0, credentials.GetCount);
	}

	private static Task<IProtocolSession> Connect(
		string hostAddress,
		SerialConnectionOptions? options = null,
		FakeCredentialSource? credentials = null,
		CancellationToken cancellationToken = default)
	{
		SerialProtocolProvider provider = new(new FakeSettingsService(), TimeProvider.System, NullLoggerFactory.Instance);
		HostProfile host = new() { Id = Guid.NewGuid(), Address = hostAddress };
		ProtocolConnectContext context = new()
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = SerialProtocolProvider.ProtocolId,
				AuthenticationMethod = AuthenticationMethod.Anonymous,
				Options = (options ?? SerialConnectionOptions.Default).ApplyTo(ProtocolOptions.Empty),
			},
			Port = SerialProtocolProvider.NoPort,
			Credentials = credentials ?? new FakeCredentialSource(),
			HostVerifier = new RefusingHostVerifier(),
			Interaction = new ThrowingInteraction(),
		};

		return provider.ConnectAsync(context, cancellationToken);
	}
}
