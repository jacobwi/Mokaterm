namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>What runs beside one connection and may decide that it stopped answering.</summary>
internal interface IConnectionWatch : IAsyncDisposable
{
	/// <summary>Completes once the connection counts as lost. Never faults, and never completes while nothing judges it.</summary>
	Task<KeepAliveTimeoutException> Lost { get; }
}
