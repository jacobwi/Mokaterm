using System.Text;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>
/// Turns what the terminal sends into keys: runs of printable text, control keys and the arrow keys the line editor uses.
/// Every other escape sequence is swallowed whole, so function keys and Alt combinations never leak into the line.
/// Sequences split across writes carry over to the next call.
/// </summary>
internal sealed class TerminalKeyReader
{
	private const char Escape = '\u001b';

	private readonly StringBuilder _text = new();
	private State _state;
	private bool _hasParameters;
	private bool _afterCarriageReturn;

	private enum State
	{
		Ground,
		Escape,
		ControlSequence,
		SingleShift,
	}

	public List<TerminalKey> Read(ReadOnlySpan<char> input)
	{
		List<TerminalKey> keys = [];
		foreach (char c in input)
		{
			switch (_state)
			{
				case State.Escape:
					_state = c switch
					{
						'[' => State.ControlSequence,
						'O' => State.SingleShift,
						_ => State.Ground,
					};
					_hasParameters = false;
					continue;

				case State.ControlSequence when c is >= '0' and <= '?':
					_hasParameters = true;
					continue;

				case State.ControlSequence when c is >= ' ' and <= '/':
					continue;

				case State.ControlSequence when c is >= '@' and <= '~':
					_state = State.Ground;
					AddArrow(keys, c, _hasParameters);
					continue;

				case State.ControlSequence:
					// Not a valid sequence byte: the sequence was cut off, so treat the character as typed.
					_state = State.Ground;
					break;

				case State.SingleShift:
					_state = State.Ground;
					AddArrow(keys, c, hasParameters: false);
					continue;
			}

			ReadGround(c, keys);
		}

		FlushText(keys);
		return keys;
	}

	private static void AddArrow(List<TerminalKey> keys, char final, bool hasParameters)
	{
		// Modified arrows (Ctrl+Up sends ESC [1;5A) do nothing, as in a plain bash.
		if (hasParameters)
		{
			return;
		}

		if (final == 'A')
		{
			keys.Add(new TerminalKey(TerminalKeyKind.HistoryPrevious));
		}
		else if (final == 'B')
		{
			keys.Add(new TerminalKey(TerminalKeyKind.HistoryNext));
		}
	}

	private void ReadGround(char c, List<TerminalKey> keys)
	{
		bool afterCarriageReturn = _afterCarriageReturn;
		_afterCarriageReturn = c == '\r';
		if (c >= ' ' && c != '\u007f' && c is not (>= '\u0080' and <= '\u009f'))
		{
			_text.Append(c);
			return;
		}

		FlushText(keys);
		switch (c)
		{
			case '\r':
				keys.Add(new TerminalKey(TerminalKeyKind.Enter));
				break;
			case '\n' when !afterCarriageReturn:
				keys.Add(new TerminalKey(TerminalKeyKind.Enter));
				break;
			case '\u007f' or '\b':
				keys.Add(new TerminalKey(TerminalKeyKind.Backspace));
				break;
			case '\t':
				keys.Add(new TerminalKey(TerminalKeyKind.Tab));
				break;
			case '\u0003':
				keys.Add(new TerminalKey(TerminalKeyKind.Interrupt));
				break;
			case '\u0004':
				keys.Add(new TerminalKey(TerminalKeyKind.EndOfFile));
				break;
			case '\u000c':
				keys.Add(new TerminalKey(TerminalKeyKind.ClearScreen));
				break;
			case Escape:
				_state = State.Escape;
				break;
		}
	}

	private void FlushText(List<TerminalKey> keys)
	{
		if (_text.Length > 0)
		{
			keys.Add(new TerminalKey(TerminalKeyKind.Text, _text.ToString()));
			_text.Clear();
		}
	}
}
