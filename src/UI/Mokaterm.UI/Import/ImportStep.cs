namespace Mokaterm.UI.Import;

/// <summary>Where the import wizard is: picking a source, ticking entries, or reading the outcome.</summary>
internal enum ImportStep
{
	Source,
	Preview,
	Done,
}
