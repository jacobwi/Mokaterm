using System.Globalization;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The desktop sizes this client holds. The server has the last word on the size, when the session starts and on
/// every resize, and the decoded desktop costs width times height times four bytes in IronRDP and the same again in
/// .NET: a 65535 x 65535 desktop would ask both for 16 GiB.
/// </summary>
internal static class RdpDesktopBounds
{
	public static bool IsSupported(int width, int height) =>
		width is > 0 and <= RdpConnectionOptions.MaxDesktopSize && height is > 0 and <= RdpConnectionOptions.MaxDesktopSize;

	/// <summary>Why a desktop of this size is refused, worded for the user.</summary>
	public static string DescribeRefusal(int width, int height) => string.Create(
		CultureInfo.InvariantCulture,
		$"The server asked for a {width} x {height} desktop. Mokaterm shows desktops of up to {RdpConnectionOptions.MaxDesktopSize} x {RdpConnectionOptions.MaxDesktopSize}.");
}
