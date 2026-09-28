using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Properties;

/// <summary>
/// Permission bits being edited for one or more entries. A bit every entry agrees on is known; the others stay mixed until
/// the user sets them, and a change leaves mixed bits as each entry has them. The octal text follows the bits, and typing
/// a valid mode sets all of them.
/// </summary>
internal sealed class ModeEditor
{
	private const UnixFileMode AllBits = PermissionChange.AllBits;

	// The bits behind each character of ls-style text: the third character of a class also shows its special bit.
	private static readonly UnixFileMode[] SymbolicBits =
	[
		UnixFileMode.UserRead,
		UnixFileMode.UserWrite,
		UnixFileMode.UserExecute | UnixFileMode.SetUser,
		UnixFileMode.GroupRead,
		UnixFileMode.GroupWrite,
		UnixFileMode.GroupExecute | UnixFileMode.SetGroup,
		UnixFileMode.OtherRead,
		UnixFileMode.OtherWrite,
		UnixFileMode.OtherExecute | UnixFileMode.StickyBit,
	];

	private readonly UnixFileMode _initialValue;
	private readonly UnixFileMode _initialKnown;

	/// <param name="modes">The mode of each entry, null where the server did not report one.</param>
	public ModeEditor(IEnumerable<UnixFileMode?> modes)
	{
		UnixFileMode? reference = null;
		UnixFileMode agreed = AllBits;
		bool unreported = false;
		foreach (UnixFileMode? mode in modes)
		{
			if (mode is not { } value)
			{
				unreported = true;
			}
			else if (reference is not { } first)
			{
				reference = value & AllBits;
			}
			else
			{
				agreed &= ~(value ^ first);
			}
		}

		_initialKnown = unreported || reference is null ? UnixFileMode.None : agreed & AllBits;
		_initialValue = (reference ?? UnixFileMode.None) & _initialKnown;
		Known = _initialKnown;
		Value = _initialValue;
		OctalText = IsMixed ? "" : UnixFileModeFormat.ToOctal(Value);
	}

	/// <summary>The bits the change controls: those the entries agreed on and those the user set since.</summary>
	public UnixFileMode Known { get; private set; }

	/// <summary>The values of the <see cref="Known"/> bits. Every other bit is clear.</summary>
	public UnixFileMode Value { get; private set; }

	/// <summary>The entries started out with different modes, or with none reported.</summary>
	public bool WasMixed => _initialKnown != AllBits;

	public bool IsMixed => Known != AllBits;

	/// <summary>The mode in octal, or empty while any bit is mixed.</summary>
	public string OctalText { get; private set; }

	/// <summary>False while the octal field holds something that is not a mode.</summary>
	public bool IsOctalValid { get; private set; } = true;

	public bool IsChanged => Known != _initialKnown || Value != _initialValue;

	/// <summary><c>rwxr-x---</c> like ls, with <c>?</c> where a bit is still mixed.</summary>
	public string Symbolic
	{
		get
		{
			char[] text = UnixFileModeFormat.ToSymbolic(Value).ToCharArray();
			for (int index = 0; index < text.Length; index++)
			{
				if ((Known & SymbolicBits[index]) != SymbolicBits[index])
				{
					text[index] = '?';
				}
			}

			return new string(text);
		}
	}

	/// <summary>Whether <paramref name="bit"/> is set, or null while it is mixed.</summary>
	public bool? Get(UnixFileMode bit) => (Known & bit) == bit ? (Value & bit) == bit : null;

	public void Set(UnixFileMode bit, bool value)
	{
		Known |= bit & AllBits;
		Value = value ? Value | (bit & AllBits) : Value & ~bit;
		OctalText = IsMixed ? "" : UnixFileModeFormat.ToOctal(Value);
		IsOctalValid = true;
	}

	public void SetOctal(string? text)
	{
		OctalText = text ?? "";
		if (UnixFileModeFormat.TryParseOctal(text, out UnixFileMode mode))
		{
			Known = AllBits;
			Value = mode & AllBits;
			IsOctalValid = true;
		}
		else
		{
			// While bits are mixed the field may stay empty; anything else has to be a mode.
			IsOctalValid = IsMixed && string.IsNullOrWhiteSpace(text);
		}
	}

	/// <summary>The known bits as a change that leaves the mixed ones as each entry has them.</summary>
	public PermissionChange ToChange(bool recursive, PermissionTargets targets, bool conditionalExecute) => new()
	{
		Mode = Value,
		Mask = Known,
		Recursive = recursive,
		Targets = targets,
		ConditionalExecute = conditionalExecute,
	};
}
