using System.Runtime.InteropServices;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Win32 <c>POINTL</c>, which is also <c>POINT</c>: two signed 32 bit coordinates in that order. Only used behind a
/// pointer, for <c>ScreenToClient</c>, which rewrites it in place; that is also why it is not readonly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal record struct PointL(int X, int Y);
