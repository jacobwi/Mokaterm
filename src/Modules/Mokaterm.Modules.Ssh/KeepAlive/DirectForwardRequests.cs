namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>Forward requests run as SSH.NET runs them, for a connection whose session could not be reached.</summary>
internal sealed class DirectForwardRequests : IForwardRequests
{
	public static readonly DirectForwardRequests Instance = new();

	private DirectForwardRequests()
	{
	}

	public Task StartAsync(Action start, CancellationToken cancellationToken) => Task.Run(start, cancellationToken);

	public Task StopAsync(Action stop, bool sendsRequest) => Task.Run(stop);
}
