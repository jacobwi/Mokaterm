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
		IEnumerable<SessionToolDescriptor> sessionTools)
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
	}

	public IReadOnlyList<ProtocolUiDescriptor> Protocols { get; }

	public IReadOnlyList<SettingsPageDescriptor> SettingsPages { get; }

	public IReadOnlyList<SessionToolDescriptor> SessionTools { get; }

	public ProtocolUiDescriptor? FindProtocol(string protocolId) =>
		_protocolsById.GetValueOrDefault(protocolId);

	public IReadOnlyList<SessionToolDescriptor> FindSessionTools(string protocolId) =>
		_toolsByProtocol.GetValueOrDefault(protocolId) ?? [];

	public MokaIconDefinition GetProtocolIcon(string protocolId) =>
		FindProtocol(protocolId)?.Icon ?? MokatermIcons.Server;
}
