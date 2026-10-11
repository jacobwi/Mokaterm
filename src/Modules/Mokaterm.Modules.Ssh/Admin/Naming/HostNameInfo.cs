namespace Mokaterm.Modules.Ssh.Admin.Naming;

/// <summary>The machine's names, and what hostnamectl adds about it.</summary>
internal sealed record HostNameInfo
{
	/// <summary>The name the kernel uses right now.</summary>
	public string Current { get; init; } = "";

	/// <summary>The name kept in <c>/etc/hostname</c> for the next boot.</summary>
	public string Static { get; init; } = "";

	/// <summary>A free-form name for people, when hostnamectl has one.</summary>
	public string Pretty { get; init; } = "";

	/// <summary>The rest of <c>hostnamectl status</c>: chassis, virtualization, machine id and so on, in its order.</summary>
	public IReadOnlyList<(string Key, string Value)> Details { get; init; } = [];
}
