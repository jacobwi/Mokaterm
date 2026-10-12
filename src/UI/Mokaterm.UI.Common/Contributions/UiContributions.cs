using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.Common.Contributions;

internal sealed class UiContributions : IUiContributions
{
	private readonly Dictionary<string, ProtocolUiDescriptor> _protocolsById;
	private readonly Dictionary<string, SessionToolDescriptor[]> _toolsByProtocol;

	public UiContributions(
		IEnumerable<ProtocolUiDescriptor> protocols,
		IEnumerable<SettingsPageDescriptor> settingsPages,
		IEnumerable<SessionToolDescriptor> sessionTools,
		IEnumerable<SystemPageDescriptor>? systemPages = null,
		IEnumerable<DockerPageDescriptor>? dockerPages = null,
		IEnumerable<AppToolDescriptor>? appTools = null)
	{
		Protocols = [.. protocols];
		_protocolsById = new Dictionary<string, ProtocolUiDescriptor>(StringComparer.OrdinalIgnoreCase);
		foreach (ProtocolUiDescriptor descriptor in Protocols)
		{
			_protocolsById[descriptor.ProtocolId] = descriptor;
		}

		SettingsPages = [.. settingsPages
			.GroupBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.Last())
			.OrderBy(page => page.Group)
			.ThenBy(page => page.Order)
			.ThenBy(page => page.Title, StringComparer.CurrentCultureIgnoreCase)];

		SessionTools = [.. sessionTools
			.OrderBy(tool => tool.Order)
			.ThenBy(tool => tool.Title, StringComparer.CurrentCultureIgnoreCase)];
		_toolsByProtocol = SessionTools
			.GroupBy(tool => tool.ProtocolId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

		SystemPages = Ordered(systemPages ?? [], page => page.Id, page => page.Order, page => page.Title);
		DockerPages = Ordered(dockerPages ?? [], page => page.Id, page => page.Order, page => page.Title);
		AppTools = Ordered(appTools ?? [], tool => tool.Id, tool => tool.Order, tool => tool.Title);
	}

	public IReadOnlyList<ProtocolUiDescriptor> Protocols { get; }

	public IReadOnlyList<SettingsPageDescriptor> SettingsPages { get; }

	public IReadOnlyList<SessionToolDescriptor> SessionTools { get; }

	public IReadOnlyList<SystemPageDescriptor> SystemPages { get; }

	public IReadOnlyList<DockerPageDescriptor> DockerPages { get; }

	public IReadOnlyList<AppToolDescriptor> AppTools { get; }

	public ProtocolUiDescriptor? FindProtocol(string protocolId) =>
		_protocolsById.GetValueOrDefault(protocolId);

	public IReadOnlyList<SessionToolDescriptor> FindSessionTools(string protocolId) =>
		_toolsByProtocol.GetValueOrDefault(protocolId) ?? [];

	public MokaIconDefinition GetProtocolIcon(string protocolId) =>
		FindProtocol(protocolId)?.Icon ?? MokatermIcons.Server;

	/// <summary>One per id, the last registration winning, so a plugin loaded after a module can take a page's place.</summary>
	private static TDescriptor[] Ordered<TDescriptor>(
		IEnumerable<TDescriptor> descriptors,
		Func<TDescriptor, string> id,
		Func<TDescriptor, int> order,
		Func<TDescriptor, string> title) =>
		[.. descriptors
			.GroupBy(id, StringComparer.Ordinal)
			.Select(group => group.Last())
			.OrderBy(order)
			.ThenBy(title, StringComparer.CurrentCultureIgnoreCase)];
}
