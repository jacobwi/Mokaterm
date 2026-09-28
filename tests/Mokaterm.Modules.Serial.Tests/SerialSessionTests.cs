using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.Modules.Serial.Sessions;
using Mokaterm.Modules.Serial.Tests.Fakes;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialSessionTests
{
	private const int TestTimeoutMilliseconds = 30_000;

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task GetFeature_OffersTheTerminalAndTheLine_AndNothingElse()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		(FakeSerialLink link, SerialTerminalChannel channel) = Create();
		await using SerialSession session = new(Guid.NewGuid(), link.PortName, channel, NullLogger<SerialSession>.Instance);

		Assert.Same(channel, session.GetFeature<ITerminalChannel>());
		Assert.Same(channel, session.GetFeature<ISerialPortFeature>());

		// A serial line carries no files.
		Assert.Null(session.GetFeature<IFileSystemFeature>());
		Assert.False(session.Completion.IsCompleted);
		Assert.Equal(0, await channel.ReadAsync(Memory<byte>.Empty, token));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Completion_Faults_WhenTheDeviceGoesAway()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		(FakeSerialLink link, SerialTerminalChannel channel) = Create();
		await using SerialSession session = new(Guid.NewGuid(), link.PortName, channel, NullLogger<SerialSession>.Instance);

		link.Device.Unplug();

		SerialProtocolException failure = await Assert.ThrowsAsync<SerialProtocolException>(
			() => Eventually.CompletesAsync(session.Completion));
		Assert.Contains(link.PortName, failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Completion_Finishes_WhenThePortClosesCleanly()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		(FakeSerialLink link, SerialTerminalChannel channel) = Create();
		await using SerialSession session = new(Guid.NewGuid(), link.PortName, channel, NullLogger<SerialSession>.Instance);

		link.Device.CloseLine();

		await Eventually.CompletesAsync(session.Completion);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Dispose_ClosesThePortAndCompletesTheSession()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		(FakeSerialLink link, SerialTerminalChannel channel) = Create();
		SerialSession session = new(Guid.NewGuid(), link.PortName, channel, NullLogger<SerialSession>.Instance);

		await session.DisposeAsync();

		await Eventually.CompletesAsync(session.Completion);
		Assert.Equal(1, link.DisposeCount);
	}

	private static (FakeSerialLink Link, SerialTerminalChannel Channel) Create()
	{
		FakeSerialLink link = new();
		SerialTerminalChannel channel = new(
			link,
			new SerialChannelOptions
			{
				Encoding = Encoding.UTF8,

				// No device check: these tests end the line themselves.
				DeviceCheck = TimeSpan.Zero,
			},
			NullLogger<SerialTerminalChannel>.Instance);
		channel.Start();
		return (link, channel);
	}
}
