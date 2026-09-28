using System.Text;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>
/// The line typed at the prompt, with bash-style history recall. Each edit returns the text to echo. Like the rest of the
/// demo shell it assumes the prompt and the line fit on one row.
/// </summary>
internal sealed class LineEditor
{
	private readonly StringBuilder _line = new();
	private readonly IReadOnlyList<string> _history;

	// Index into the history while recalling, or -1 while the line is new.
	private int _recalled = -1;
	private string _draft = "";

	public LineEditor(IReadOnlyList<string> history) => _history = history;

	public string Text => _line.ToString();

	public bool IsEmpty => _line.Length == 0;

	public string Insert(string text)
	{
		_line.Append(text);
		return text;
	}

	public string Backspace()
	{
		if (_line.Length == 0)
		{
			return "";
		}

		int last = _line.Length - 1;
		bool surrogatePair = last > 0 && char.IsLowSurrogate(_line[last]) && char.IsHighSurrogate(_line[last - 1]);
		_line.Length -= surrogatePair ? 2 : 1;
		return "\b \b";
	}

	/// <summary>Returns the finished line and starts an empty one.</summary>
	public string Submit()
	{
		string line = _line.ToString();
		Discard();
		return line;
	}

	public void Discard()
	{
		_line.Clear();
		_recalled = -1;
		_draft = "";
	}

	public string Previous(string prompt)
	{
		if (_history.Count == 0 || _recalled == 0)
		{
			return "";
		}

		if (_recalled < 0)
		{
			_draft = _line.ToString();
			_recalled = _history.Count;
		}

		_recalled--;
		return Replace(prompt, _history[_recalled]);
	}

	public string Next(string prompt)
	{
		if (_recalled < 0)
		{
			return "";
		}

		_recalled++;
		if (_recalled < _history.Count)
		{
			return Replace(prompt, _history[_recalled]);
		}

		// Past the newest entry the line typed before recalling comes back.
		_recalled = -1;
		return Replace(prompt, _draft);
	}

	private string Replace(string prompt, string text)
	{
		_line.Clear().Append(text);
		return "\r" + Ansi.EraseLine + prompt + text;
	}
}
