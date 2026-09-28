namespace Mokaterm.Abstractions.Import;

/// <summary>Where an import source takes its entries from.</summary>
public enum ImportSourceKind
{
	/// <summary>A client installed on this machine: a config file in the user profile or the registry.</summary>
	Installed,

	/// <summary>A file the user picks, such as an exported <c>.reg</c> or a portable ini.</summary>
	File,
}
