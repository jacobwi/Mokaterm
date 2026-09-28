using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.Modules.Serial.Tests.Fakes;

namespace Mokaterm.Modules.Serial.Tests;

/// <summary>
/// Runs against a real port when MOKATERM_TEST_SERIAL names one: <c>COM5</c>, <c>/dev/ttyUSB0</c>, or a name with a
/// baud rate behind it (<c>COM5@9600</c>). A loopback adapter sends everything straight back, which the test checks
/// when it sees it; one end of a virtual pair (com0com, socat) sends nowhere, which is fine too. Nothing else on this
/// machine may hold the port: a serial port is opened by one program at a time.
/// </summary>
public sealed class SerialLiveDeviceTests
{
	private const string Variable = "MOKATERM_TEST_SERIAL";

	private const int TestTimeoutMilliseconds = TestTimeouts.NetworkMilliseconds;

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Connect_OpensThePortAndCarriesBytesBothWays()
	{
		// The name is a port, not an endpoint, so only the read and the skip are shared.
		string setting = LiveEndpoint.Require(Variable, $"Set {Variable}=COM5 (optionally COM5@9600) to run against a real serial port.");

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		(string port, int baudRate) = Parse(setting);
		SerialConnectionOptions options = SerialConnectionOptions.Default with
		{
			Line = SerialLineSettings.Default with { BaudRate = baudRate },
		};

		SerialProtocolProvider provider = new(new FakeSettingsService(), TimeProvider.System, NullLoggerFactory.Instance);
		RecordingProgress<string> status = new();
		await using IProtocolSession session = await provider.ConnectAsync(Context(port, options, status), cancellationToken);

		ITerminalChannel terminal = session.GetFeature<ITerminalChannel>()
			?? throw new InvalidOperationException("A serial session must expose a terminal channel.");
		ISerialPortFeature line = session.GetFeature<ISerialPortFeature>()
			?? throw new InvalidOperationException("A serial session must expose its line.");

		Assert.Contains(status.Reports, report => report.Contains(port, StringComparison.OrdinalIgnoreCase));
		Assert.Equal(port, line.Status.PortName);
		Assert.Equal(baudRate, line.Status.Line.BaudRate);
		Assert.True(line.Status.IsOpen);

		// The pins, and the two this end drives. A device that reports none says so instead of failing.
		await line.RefreshAsync(cancellationToken);
		await line.SetDtrAsync(false, cancellationToken);
		await line.SetDtrAsync(true, cancellationToken);
		await line.SendBreakAsync(cancellationToken);

		// A loopback adapter returns this; a virtual pair sends it to the other end, where nobody is listening.
		await terminal.WriteAsync("mokaterm\r"u8.ToArray(), cancellationToken);
		byte[] buffer = new byte[256];
		using CancellationTokenSource quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		quiet.CancelAfter(TimeSpan.FromSeconds(2));
		try
		{
			int read = await terminal.ReadAsync(buffer, quiet.Token);
			Assert.True(read > 0);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Nothing came back, which is what a port with no loopback does.
		}

		Assert.Equal(0, line.Status.WriteTimeouts);
		Assert.False(session.Completion.IsCompleted);
	}

	private static (string Port, int BaudRate) Parse(string setting)
	{
		string value = setting.Trim();
		int at = value.LastIndexOf('@');
		if (at < 0)
		{
			return (SerialPortNames.Normalize(value), SerialLineSettings.Default.BaudRate);
		}

		int baudRate = int.TryParse(value[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
			? parsed
			: SerialLineSettings.Default.BaudRate;
		return (SerialPortNames.Normalize(value[..at]), baudRate);
	}

	private static ProtocolConnectContext Context(string port, SerialConnectionOptions options, IProgress<string> status)
	{
		HostProfile host = new() { Id = Guid.NewGuid(), Address = port };
		return new ProtocolConnectContext
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = SerialProtocolProvider.ProtocolId,
				AuthenticationMethod = AuthenticationMethod.Anonymous,
				Options = options.ApplyTo(ProtocolOptions.Empty),
			},

			// A serial line has no port number; the provider never looks at this one.
			Port = SerialProtocolProvider.NoPort,
			Credentials = new FakeCredentialSource(),
			HostVerifier = new RefusingHostVerifier(),
			Interaction = new ThrowingInteraction(),
			Status = status,
			TerminalSize = new TerminalSize(100, 40),
		};
	}
}
