using System.Text;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The anchor a <see cref="SettingsRow"/> carries in its <c>data-setting</c> attribute, made from its label:
/// <c>Right-click</c> is <c>right-click</c> and <c>.NET runtime</c> is <c>net-runtime</c>. The row and the search both
/// ask here, so a result always finds its row.
/// </summary>
public static class SettingsAnchor
{
	public static string Of(string label)
	{
		ArgumentNullException.ThrowIfNull(label);
		StringBuilder anchor = new(label.Length);
		bool pendingDash = false;
		foreach (char character in label)
		{
			if (char.IsLetterOrDigit(character))
			{
				if (pendingDash && anchor.Length > 0)
				{
					anchor.Append('-');
				}

				anchor.Append(char.ToLowerInvariant(character));
				pendingDash = false;
			}
			else
			{
				pendingDash = true;
			}
		}

		return anchor.ToString();
	}
}
