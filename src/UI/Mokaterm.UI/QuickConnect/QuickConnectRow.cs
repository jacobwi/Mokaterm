using System.Globalization;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// One row under the quick connect box. Saved and recent rows both carry a host and a login, so the list draws them
/// with the same label the tabs use; a recent row's are made up for the drawing and never saved.
/// </summary>
/// <remarks>A class and not a record: rows hold host and user names, which a record would print.</remarks>
public sealed class QuickConnectRow
{
	private QuickConnectRow(QuickConnectRowKind kind, string key)
	{
		Kind = kind;
		Key = key;
	}

	public QuickConnectRowKind Kind { get; }

	/// <summary>Stays the same for one target while the list is re-ranked, for <c>@key</c>.</summary>
	public string Key { get; }

	public string ProtocolId { get; private init; } = "";

	public QuickConnectRank Rank { get; private init; } = QuickConnectRank.None;

	public HostProfile? Host { get; private init; }

	public ConnectionProfile? Connection { get; private init; }

	public QuickConnectRecent? Recent { get; private init; }

	public QuickConnectSuggestion? Suggestion { get; private init; }

	/// <summary>The text Tab puts in the box for this row, or null for an action.</summary>
	public string? Completion { get; private init; }

	public bool IsAction => Kind is QuickConnectRowKind.ConnectAndSave or QuickConnectRowKind.ClearHistory;

	/// <summary>
	/// The protocol, user, address and port, for telling two rows for the same target apart from two different ones.
	/// Null for an action.
	/// </summary>
	internal string? TargetKey { get; private init; }

	internal static QuickConnectRow ForLogin(HostProfile host, ConnectionProfile login, ProtocolDescriptor? protocol, QuickConnectRank rank)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(login);
		int? port = protocol?.UsesPort == false ? null : login.Port;
		return new QuickConnectRow(QuickConnectRowKind.SavedLogin, "login:" + login.Id.ToString("N"))
		{
			ProtocolId = login.ProtocolId,
			Rank = rank,
			Host = host,
			Connection = login,
			Completion = QuickConnectText.Format(login.Username, host.Address, port),
			TargetKey = TargetKeyOf(login.ProtocolId, login.Username, host.Address, port),
		};
	}

	/// <param name="savedHost">A saved host at the same address, whose colour the row borrows so the machine looks the same.</param>
	internal static QuickConnectRow ForRecent(QuickConnectRecent recent, HostProfile? savedHost, QuickConnectRank rank)
	{
		ArgumentNullException.ThrowIfNull(recent);
		string targetKey = TargetKeyOf(recent.ProtocolId, recent.Username, recent.Address, recent.Port);
		return new QuickConnectRow(QuickConnectRowKind.Recent, "recent:" + targetKey)
		{
			ProtocolId = recent.ProtocolId,
			Rank = rank,
			Recent = recent,
			Host = new HostProfile { Id = Guid.Empty, Address = recent.Address, Color = savedHost?.Color },
			Connection = new ConnectionProfile
			{
				Id = Guid.Empty,
				HostId = Guid.Empty,
				ProtocolId = recent.ProtocolId,
				Username = recent.Username,
				Port = recent.Port,
			},
			Completion = recent.Text,
			TargetKey = targetKey,
		};
	}

	internal static QuickConnectRow ForDetected(string protocolId, QuickConnectSuggestion suggestion, QuickConnectRank rank)
	{
		ArgumentNullException.ThrowIfNull(protocolId);
		ArgumentNullException.ThrowIfNull(suggestion);
		string targetKey = TargetKeyOf(protocolId, null, suggestion.Address, null);
		return new QuickConnectRow(QuickConnectRowKind.Detected, "detected:" + targetKey)
		{
			ProtocolId = protocolId,
			Rank = rank,
			Suggestion = suggestion,
			Completion = suggestion.Address,
			TargetKey = targetKey,
		};
	}

	internal static QuickConnectRow ConnectAndSave() => new(QuickConnectRowKind.ConnectAndSave, "action:save");

	internal static QuickConnectRow ClearHistory() => new(QuickConnectRowKind.ClearHistory, "action:clear");

	private static string TargetKeyOf(string protocolId, string? username, string address, int? port) =>
		string.Join(
			'\n',
			protocolId.ToLowerInvariant(),
			(username ?? "").ToLowerInvariant(),
			address.ToLowerInvariant(),
			port?.ToString(CultureInfo.InvariantCulture) ?? "");
}
