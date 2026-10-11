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

	// Quick connect asks the module about a typed port name, so the box refuses what the connect would refuse.
	[Theory]
	[InlineData("COM3", true, true)]
	[InlineData(@"\\.\COM12", true, true)]
	[InlineData("/dev/ttyUSB0", false, true)]
	[InlineData("COM 3", false, false)]
	public void ValidateQuickConnectAddress_AppliesThePortNameRule(string name, bool onWindows, bool elsewhere)
	{
		IQuickConnectSuggestions provider = new SerialProtocolProvider(new FakeSettingsService(), TimeProvider.System, NullLoggerFactory.Instance);
		bool expected = OperatingSystem.IsWindows() ? onWindows : elsewhere;

		string? problem = provider.ValidateQuickConnectAddress(name);

		Assert.Equal(expected, problem is null);
		Assert.Equal(SerialPortNames.Validate(name), problem);
	}

	[Fact]
	public void GetQuickConnectSuggestions_ListsThePortsThisMachineHas()
	{
		SerialProtocolProvider provider = new(new FakeSettingsService(), TimeProvider.System, NullLoggerFactory.Instance);

		Assert.Equal(SerialPortNames.List(), provider.GetQuickConnectSuggestions().Select(suggestion => suggestion.Address));
		Assert.All(provider.GetQuickConnectSuggestions(), suggestion => Assert.Null(suggestion.Description));
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
