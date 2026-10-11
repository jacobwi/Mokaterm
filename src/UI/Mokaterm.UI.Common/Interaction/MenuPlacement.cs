using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Interaction;

/// <summary>
/// Opens a Moka context menu where the user is. A click the keyboard caused (Enter or Space on a button, the menu key on
/// a row) carries no pointer position, so Moka would open the menu in the top left corner of the page; this opens it under
/// the focused element instead, which is the button or row the keyboard was on. Use it for every menu a button or a row
/// opens, in place of <see cref="IMokaContextMenuService.Show(MouseEventArgs, IReadOnlyList{MokaContextMenuItem})"/>.
/// </summary>
public sealed class MenuPlacement
{
	private readonly IMokaContextMenuService _menu;
	private readonly FormInterop _forms;

	public MenuPlacement(IMokaContextMenuService menu, FormInterop forms)
	{
		_menu = menu;
		_forms = forms;
	}

	public void Show(MouseEventArgs args, IReadOnlyList<MokaContextMenuItem> items)
	{
		ArgumentNullException.ThrowIfNull(args);
		ArgumentNullException.ThrowIfNull(items);
		if (HasPointer(args))
		{
			_menu.Show(args, items);
			return;
		}

		_ = ShowUnderFocusAsync(args, items);
	}

	// A real pointer can only report (0, 0) from the very corner of the page, where nothing opens a menu.
	internal static bool HasPointer(MouseEventArgs args) => args.ClientX != 0 || args.ClientY != 0;

	private async Task ShowUnderFocusAsync(MouseEventArgs args, IReadOnlyList<MokaContextMenuItem> items)
	{
		if (await _forms.FocusedBoxAsync() is { } box)
		{
			_menu.Show(box.Left, box.Bottom, items, openedFromKeyboard: true, anchorTop: box.Top);
		}
		else
		{
			_menu.Show(args, items);
		}
	}
}
