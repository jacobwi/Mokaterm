using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// OLE's <c>IDropTarget</c>, implemented here rather than called: <c>RegisterDragDrop</c> hands one to the shell, which
/// calls these while a drag is over the window. <c>POINTL</c> crosses as the single 64 bit value it is instead of a
/// struct, the shape .NET's own OLE interop has always used, and <see cref="NativeDropPoint.ScreenPoint"/> takes it
/// apart. No <c>PreserveSig</c>, so the runtime's wrapper turns anything that still escapes into an HRESULT instead of
/// letting it unwind through the shell.
/// </summary>
[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
	/// <param name="effect">What the source allows on the way in, what we will do on the way out.</param>
	void DragEnter(IDataObject? data, uint keyState, long point, ref uint effect);

	void DragOver(uint keyState, long point, ref uint effect);

	void DragLeave();

	void Drop(IDataObject? data, uint keyState, long point, ref uint effect);
}
