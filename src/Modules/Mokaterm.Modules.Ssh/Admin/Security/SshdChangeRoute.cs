namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>Where a changed sshd setting goes: the security page's own drop-in, or a file the user edits.</summary>
/// <param name="UsesDropIn">True when <see cref="SshdConfig.DropInFile"/> wins; false when <paramref name="File"/> has to be edited.</param>
/// <param name="File">The drop-in, or the file that sets the keyword now (the main file when none does).</param>
internal sealed record SshdChangeRoute(bool UsesDropIn, string File);
