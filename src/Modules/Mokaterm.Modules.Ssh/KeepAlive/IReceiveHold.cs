namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>
/// Something of ours that can hold up SSH.NET's message loop, as the shell's flow control does while a view falls behind.
/// While it is held no reply can be read, however alive the server is, so a missing reply then proves nothing.
/// </summary>
internal interface IReceiveHold
{
	/// <summary>How many holds have started, so a hold that began and ended between two looks still shows.</summary>
	long Holds { get; }

	bool IsHeld { get; }
}
