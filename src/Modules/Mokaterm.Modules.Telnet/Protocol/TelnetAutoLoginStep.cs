namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>What the auto-login wants typed. The caller writes it and then wipes it.</summary>
/// <param name="Input">The answer as UTF-8, without a line ending; the channel adds the one the connection uses.</param>
/// <param name="Echo">False for the password, so a locally echoed session never puts it on the screen.</param>
internal readonly record struct TelnetAutoLoginStep(byte[] Input, bool Echo);
