using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>A segmented control bound to an enum, so pages never parse segment strings themselves.</summary>
public sealed partial class EnumSegmentedControl<TEnum> : ComponentBase
	where TEnum : struct, Enum
{
	[Parameter]
	public TEnum Value { get; set; }

	[Parameter]
	public EventCallback<TEnum> ValueChanged { get; set; }

	[Parameter, EditorRequired]
	public IReadOnlyList<EnumOption<TEnum>> Options { get; set; } = [];

	[Parameter]
	public string? AriaLabel { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	private async Task OnSelectedAsync(string? name)
	{
		foreach (EnumOption<TEnum> option in Options)
		{
			if (option.Value.ToString() == name)
			{
				await ValueChanged.InvokeAsync(option.Value);
				return;
			}
		}
	}
}
