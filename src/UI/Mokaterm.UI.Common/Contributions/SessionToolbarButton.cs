using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>One button the session toolbar can show, as the toolbar and the setting that hides it both name it.</summary>
/// <param name="Id">What <c>AppearanceSettings.HiddenToolbarButtons</c> stores.</param>
public sealed record SessionToolbarButton(string Id, string Title, MokaIconDefinition Icon);
