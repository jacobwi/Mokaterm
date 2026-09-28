using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>Files for a demo session: a view as the login user and a root view that opens at once, with no sudo prompt.</summary>
internal sealed class DemoFileSystemFeature : IFileSystemFeature, IAsyncDisposable
{
	private readonly DemoFileSystem _user;
	private readonly DemoFileSystem _root;

	public DemoFileSystemFeature(DemoFileTree tree, DemoAccount account, CancellationToken sessionClosed)
	{
		_user = new DemoFileSystem(tree, account, isElevated: false, sessionClosed);
		_root = new DemoFileSystem(tree, DemoAccount.Root, isElevated: true, sessionClosed);
	}

	public bool SupportsElevation => true;

	public ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IRemoteFileSystem>(_user);

	public ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IRemoteFileSystem>(_root);

	public async ValueTask DisposeAsync()
	{
		await _user.DisposeAsync();
		await _root.DisposeAsync();
	}
}
