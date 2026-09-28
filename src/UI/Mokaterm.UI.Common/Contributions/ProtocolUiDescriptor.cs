using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>How the shell presents a protocol. Registered by the module next to its protocol provider.</summary>
public sealed record ProtocolUiDescriptor
{
	public required string ProtocolId { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	/// <summary>
	/// Protocol-specific options shown in the connection editor. Must derive from
	/// <see cref="ConnectionOptionsEditorBase"/>. Variants without their own editor use their parent's.
	/// </summary>
	public Type? OptionsEditor { get; init; }

	/// <summary>
	/// A custom view for sessions the shell has no built-in layout for (remote desktop, message brokers).
	/// Must derive from <see cref="SessionViewBase"/>. Terminal and file system sessions leave this null.
	/// </summary>
	public Type? SessionView { get; init; }
}
