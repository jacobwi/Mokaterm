using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// The protocols quick connect has to tell apart: ones with a host and a port, and ones whose address names something
/// on this computer, with and without names of their own to offer. Nothing here connects.
/// </summary>
internal sealed class QuickConnectProtocols : IProtocolRegistry
{
	public static readonly ProtocolDescriptor Ssh = Descriptor("ssh", "SSH", 22, "user@host:port", order: 0);

	public static readonly ProtocolDescriptor Vnc = Descriptor("vnc", "VNC", 5900, "host:5900", order: 20) with
	{
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.Anonymous],
	};

	public static readonly ProtocolDescriptor Rdp = Descriptor("rdp", "RDP", 3389, "user@host", order: 30);

	public static readonly ProtocolDescriptor Serial = Descriptor("serial", "Serial", 0, "COM3", order: 60) with
	{
		UsesPort = false,
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
	};

	public static readonly ProtocolDescriptor Local = Descriptor("local", "Local shell", 0, "pwsh, cmd, wsl or a distro", order: 70) with
	{
		UsesPort = false,
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
	};

	public static readonly ProtocolDescriptor Docker = Descriptor("docker", "Docker container", 0, "container name", order: 71) with
	{
		UsesPort = false,
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
	};

	private readonly Dictionary<string, IProtocolProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

	public QuickConnectProtocols()
	{
		foreach (ProtocolDescriptor descriptor in new[] { Ssh, Vnc, Rdp, Docker })
		{
			_providers[descriptor.Id] = new PlainProvider(descriptor);
		}

		// The serial module's own rule, cut down: a name with a slash is a device path, and only COM names pass here.
		SerialPorts = new SuggestingProvider(
			Serial,
			[new QuickConnectSuggestion("COM3"), new QuickConnectSuggestion("COM10")],
			address => address.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || address.StartsWith('/')
				? null
				: "On Windows a port name is COM followed by its number, for example COM3.");
		_providers[Serial.Id] = SerialPorts;
		Shells = new SuggestingProvider(
			Local,
			[new QuickConnectSuggestion("pwsh", "PowerShell"), new QuickConnectSuggestion("Ubuntu", "WSL distribution")],
			_ => null);
		_providers[Local.Id] = Shells;
	}

	public SuggestingProvider SerialPorts { get; }

	public SuggestingProvider Shells { get; }

	public IReadOnlyList<ProtocolDescriptor> Protocols =>
		[.. _providers.Values.Select(provider => provider.Descriptor).OrderBy(descriptor => descriptor.Order)];

	public IProtocolProvider? Find(string protocolId) => _providers.GetValueOrDefault(protocolId);

	public IProtocolProvider Get(string protocolId) => Find(protocolId) ?? throw new KeyNotFoundException(protocolId);

	private static ProtocolDescriptor Descriptor(string id, string name, int port, string example, int order) => new()
	{
		Id = id,
		DisplayName = name,
		DefaultPort = port,
		QuickConnectExample = example,
		Capabilities = ProtocolCapabilities.Terminal,
		Order = order,
	};

	internal class PlainProvider(ProtocolDescriptor descriptor) : IProtocolProvider
	{
		public ProtocolDescriptor Descriptor => descriptor;

		public Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken) =>
			throw new NotSupportedException("Quick connect tests never connect.");
	}

	/// <summary>A provider that offers names and checks typed ones, the way the serial and local modules do.</summary>
	internal sealed class SuggestingProvider(
		ProtocolDescriptor descriptor,
		IReadOnlyList<QuickConnectSuggestion> names,
		Func<string, string?> validate) : PlainProvider(descriptor), IQuickConnectSuggestions
	{
		public int Asked { get; private set; }

		public IReadOnlyList<QuickConnectSuggestion> GetQuickConnectSuggestions()
		{
			Asked++;
			return names;
		}

		public string? ValidateQuickConnectAddress(string address) => validate(address);
	}
}
