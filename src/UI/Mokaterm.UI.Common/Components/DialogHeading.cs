using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// What a dialog's <see cref="DialogHeader"/> says, for a dialog opened through Moka's dialog service: the service only
/// takes a plain title, so the dialog is opened without one and its content draws the header from this.
/// </summary>
public sealed record DialogHeading(string Title, MokaIconDefinition Icon);
