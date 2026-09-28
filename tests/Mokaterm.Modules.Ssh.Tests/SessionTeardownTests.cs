using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Modules.Ssh.Sessions;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SessionTeardownTests
{
	[Fact]
	public async Task ReleaseAsync_KeepsGoing_WhenAPartFailsToClose()
	{
		List<string> closed = [];

		await SessionTeardown.ReleaseAsync(() => throw new InvalidOperationException("The shell was already gone."), NullLogger.Instance, Guid.NewGuid(), "the shell");
		await SessionTeardown.ReleaseAsync(async () => await Task.FromException(new IOException("The socket broke.")), NullLogger.Instance, Guid.NewGuid(), "the tunnels");
		await SessionTeardown.ReleaseAsync(
			() =>
			{
				closed.Add("login");
				return ValueTask.CompletedTask;
			},
			NullLogger.Instance,
			Guid.NewGuid(),
			"the login");

		Assert.Equal(["login"], closed);
	}
}
