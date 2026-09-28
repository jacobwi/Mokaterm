namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// Back and forward stacks of directory paths. Moves are committed only after the target directory loaded, so a
/// failed navigation leaves the history as it was.
/// </summary>
internal sealed class NavigationHistory
{
	private const int Limit = 100;

	private readonly List<string> _back = [];
	private readonly List<string> _forward = [];

	public bool CanGoBack => _back.Count > 0;

	public bool CanGoForward => _forward.Count > 0;

	public string? PeekBack() => _back.Count > 0 ? _back[^1] : null;

	public string? PeekForward() => _forward.Count > 0 ? _forward[^1] : null;

	/// <summary>Records a new move away from <paramref name="from"/>. A new move drops the forward history.</summary>
	public void Push(string from)
	{
		Add(_back, from);
		_forward.Clear();
	}

	/// <summary>Commits a successful move to <see cref="PeekBack"/>.</summary>
	public void CompleteBack(string from)
	{
		if (_back.Count > 0)
		{
			_back.RemoveAt(_back.Count - 1);
			Add(_forward, from);
		}
	}

	/// <summary>Commits a successful move to <see cref="PeekForward"/>.</summary>
	public void CompleteForward(string from)
	{
		if (_forward.Count > 0)
		{
			_forward.RemoveAt(_forward.Count - 1);
			Add(_back, from);
		}
	}

	public void Clear()
	{
		_back.Clear();
		_forward.Clear();
	}

	private static void Add(List<string> stack, string path)
	{
		if (stack.Count > 0 && stack[^1] == path)
		{
			return;
		}

		stack.Add(path);
		if (stack.Count > Limit)
		{
			stack.RemoveAt(0);
		}
	}
}
