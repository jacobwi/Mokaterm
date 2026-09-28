namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>Who a file operation runs as. Each account's primary group has its own name, and root skips permission checks.</summary>
internal sealed record DemoAccount(string Name, string Home)
{
	public static DemoAccount Root { get; } = new("root", "/root");

	public string Group => Name;

	public bool IsRoot => Name == Root.Name;

	public static DemoAccount ForUser(string name) => name == Root.Name ? Root : new(name, "/home/" + name);
}
