using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Base for typed settings that save while you type. Text that does not parse stays in the field with an error and
/// is never saved; leaving the field puts the saved value back.
/// </summary>
public abstract class DraftSettingBase<TValue> : ComponentBase
{
	private bool _initialized;
	private TValue _committed = default!;

	[Parameter]
	public TValue Value { get; set; } = default!;

	[Parameter]
	public EventCallback<TValue> ValueChanged { get; set; }

	/// <summary>Visible label above the field. Rows usually leave it empty and set <see cref="AriaLabel"/>.</summary>
	[Parameter]
	public string? Label { get; set; }

	[Parameter]
	public string? AriaLabel { get; set; }

	[Parameter]
	public string? Placeholder { get; set; }

	/// <summary>CSS width of the field, for example <c>120px</c>.</summary>
	[Parameter]
	public string? Width { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	protected string Text { get; private set; } = "";

	protected string? Error { get; private set; }

	protected string? WidthStyle => Width is null ? null : $"width:{Width}";

	protected abstract bool TryParse(string text, [MaybeNullWhen(false)] out TValue value, [NotNullWhen(false)] out string? error);

	protected abstract string Format(TValue value);

	protected override void OnParametersSet()
	{
		// Only a value that differs from the last one saved replaces the text, so re-renders never undo typing.
		if (!_initialized || !EqualityComparer<TValue>.Default.Equals(Value, _committed))
		{
			_initialized = true;
			_committed = Value;
			Text = Format(Value);
			Error = null;
		}
	}

	protected async Task OnTextChangedAsync(string? text)
	{
		Text = text ?? "";
		if (!TryParse(Text, out TValue? value, out string? error))
		{
			Error = error;
			return;
		}

		Error = null;
		await CommitAsync(value);
	}

	/// <summary>Saves a value picked from a list instead of typed.</summary>
	protected async Task SelectAsync(TValue value)
	{
		Text = Format(value);
		Error = null;
		await CommitAsync(value);
	}

	protected void OnBlur()
	{
		Text = Format(_committed);
		Error = null;
	}

	protected bool IsCommitted(TValue value) => EqualityComparer<TValue>.Default.Equals(value, _committed);

	private async Task CommitAsync(TValue value)
	{
		if (IsCommitted(value))
		{
			return;
		}

		_committed = value;
		await ValueChanged.InvokeAsync(value);
	}
}
