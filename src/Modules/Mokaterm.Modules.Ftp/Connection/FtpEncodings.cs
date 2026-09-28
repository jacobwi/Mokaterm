using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>Resolves the character set named in <see cref="FtpConnectionOptions.EncodingName"/>.</summary>
internal static class FtpEncodings
{
	// Legacy servers use code pages such as windows-1252 or shift_jis, which .NET only knows once this provider is
	// registered. The type initializer guarantees that happens once, before any lookup on any thread.
	static FtpEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

	public static bool TryGet(string? name, [NotNullWhen(true)] out Encoding? encoding)
	{
		encoding = null;
		string trimmed = name?.Trim() ?? "";
		if (trimmed.Length == 0)
		{
			return false;
		}

		try
		{
			encoding = Encoding.GetEncoding(trimmed);
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (NotSupportedException)
		{
			return false;
		}

		// FluentFTP only sends OPTS UTF8 ON when the encoding is the Encoding.UTF8 instance itself.
		if (encoding.CodePage == Encoding.UTF8.CodePage)
		{
			encoding = Encoding.UTF8;
		}

		return true;
	}

	/// <summary>The named character set, or UTF-8 when the name is blank or unknown.</summary>
	public static Encoding Resolve(string? name) => TryGet(name, out Encoding? encoding) ? encoding : Encoding.UTF8;
}
