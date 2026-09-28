using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Base for an options editor over one module's typed options record. It holds the record the fields read, replaces it
/// the moment a field changes and only then reports the new <see cref="ProtocolOptions"/>, so a field shows what was
/// just picked instead of waiting for the connection editor to hand the options back.
/// </summary>
public abstract class ConnectionOptionsEditorBase<TOptions> : ConnectionOptionsEditorBase
	where TOptions : class
{
	private TOptions? _current;

	/// <summary>
	/// What every field reads: the record in <see cref="ConnectionOptionsEditorBase.Options"/>, or the last change this
	/// editor reported. Read lazily as well, because a component's <c>OnInitialized</c> runs before its parameters are set.
	/// </summary>
	protected TOptions Current => _current ??= From(Options);

	/// <summary>Reads the module's record out of <paramref name="options"/>.</summary>
	protected abstract TOptions From(ProtocolOptions options);

	/// <summary>Writes <paramref name="current"/> over <paramref name="options"/>, keeping keys that belong elsewhere.</summary>
	protected abstract ProtocolOptions ApplyTo(TOptions current, ProtocolOptions options);

	/// <summary>Runs whenever <see cref="Current"/> changes, for whatever an editor derives from it.</summary>
	protected virtual void OnCurrentChanged()
	{
	}

	protected override void OnParametersSet() => Adopt(From(Options));

	/// <summary>Shows <paramref name="updated"/> and reports it.</summary>
	protected Task ChangeAsync(TOptions updated)
	{
		ArgumentNullException.ThrowIfNull(updated);
		Adopt(updated);
		return OptionsChanged.InvokeAsync(ApplyTo(updated, Options));
	}

	private void Adopt(TOptions current)
	{
		_current = current;
		OnCurrentChanged();
	}
}
