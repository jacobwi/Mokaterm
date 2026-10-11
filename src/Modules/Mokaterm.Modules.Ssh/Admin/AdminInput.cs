using System.Text;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// What an action sends on stdin, kept in pinned memory that is wiped on dispose: a new password for chpasswd must
/// never be on a command line, and a crontab goes the same way so <c>crontab -</c> never meets an editor.
/// </summary>
internal sealed class AdminInput : IDisposable
{
	private AdminInput(SecretBuffer bytes, string preview)
	{
		Bytes = bytes;
		Preview = preview;
	}

	public SecretBuffer Bytes { get; }

	/// <summary>What the confirmation shows of the input. A password is masked here, never shown.</summary>
	public string Preview { get; }

	/// <summary>Text whose every line is shown before it is sent, such as a crontab.</summary>
	public static AdminInput Visible(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string normalized = text.ReplaceLineEndings("\n");
		return new AdminInput(SecretBuffer.FromString(normalized), normalized);
	}

	/// <summary>Text that carries a secret, shown as <paramref name="preview"/> instead.</summary>
	public static AdminInput Secret(string text, string preview)
	{
		ArgumentNullException.ThrowIfNull(text);
		return new AdminInput(SecretBuffer.FromString(text), preview);
	}

	public void Dispose() => Bytes.Dispose();

	public override string ToString() => new StringBuilder("AdminInput { Bytes = ").Append(Bytes.Length).Append(" }").ToString();
}
