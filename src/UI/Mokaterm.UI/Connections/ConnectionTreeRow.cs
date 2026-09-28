using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.Connections;

internal enum ConnectionTreeRowKind
{
	Folder,
	Host,
	Login,
}

/// <summary>One visible row of the flattened connection tree.</summary>
internal sealed record ConnectionTreeRow(
	ConnectionTreeRowKind Kind,
	string Key,
	int Depth,
	bool Expandable,
	bool Expanded,
	ConnectionFolder? Folder = null,
	HostProfile? Host = null,
	ConnectionProfile? Login = null,
	int ChildCount = 0);
