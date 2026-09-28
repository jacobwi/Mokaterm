namespace Mokaterm.UI.Common.Components;

/// <summary>An input the user typed that failed validation: shown with its error instead of the saved value until corrected.</summary>
public sealed class FieldDraft<T>
{
	private bool _isSet;
	private T? _value;

	public string? Error { get; private set; }

	public T Show(T saved) => _isSet && _value is { } value ? value : saved;

	public void Reject(T value, string error)
	{
		_isSet = true;
		_value = value;
		Error = error;
	}

	public void Clear()
	{
		_isSet = false;
		_value = default;
		Error = null;
	}
}
