namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>How a security check came out.</summary>
internal enum SecurityLevel
{
	/// <summary>As it should be.</summary>
	Good,

	/// <summary>Worth knowing about, and often worth changing.</summary>
	Notice,

	/// <summary>Something that makes the machine easier to get into.</summary>
	Warning,

	/// <summary>Not read: it needs root, a tool is missing, or it is still being checked.</summary>
	Unknown,
}
