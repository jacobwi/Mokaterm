using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// iTerm2 .itermcolors files: a property list whose color entries hold 0 to 1 components. Colors are written back in
/// the sRGB color space, which is what iTerm2 writes today.
/// </summary>
internal static class ITerm2ThemeCodec
{
	private const string ColorSpace = "sRGB";
	private const string PublicId = "-//Apple//DTD PLIST 1.0//EN";
	private const string SystemId = "http://www.apple.com/DTDs/PropertyList-1.0.dtd";

	/// <summary>A color plist is four levels deep (plist, dict, color dict, component); this leaves room for wrappers.</summary>
	private const int MaxDepth = 32;

	private static readonly string[] SlotNames =
	[
		"Ansi 0 Color", "Ansi 1 Color", "Ansi 2 Color", "Ansi 3 Color", "Ansi 4 Color", "Ansi 5 Color", "Ansi 6 Color", "Ansi 7 Color",
		"Ansi 8 Color", "Ansi 9 Color", "Ansi 10 Color", "Ansi 11 Color", "Ansi 12 Color", "Ansi 13 Color", "Ansi 14 Color", "Ansi 15 Color",
	];

	public static TerminalThemeImport Read(string text, string? fileName)
	{
		Dictionary<string, XElement>? entries = ReadRootDictionary(text);
		if (entries is null)
		{
			return TerminalThemeImport.Failure("This is not a readable property list.");
		}

		TerminalThemeDraft draft = new(TerminalThemeFormat.ITerm2, "an iTerm2 file", SlotNames);
		draft.SetBackground(Color(entries, "Background Color", "Background"));
		draft.SetForeground(Color(entries, "Foreground Color", "Foreground"));
		draft.SetCursor(Color(entries, "Cursor Color", "Cursor"));
		draft.SetCursorAccent(Color(entries, "Cursor Text Color", "Cursor Text"));
		draft.SetSelectionBackground(Color(entries, "Selection Color", "Selection"));
		draft.SetSelectionForeground(Color(entries, "Selected Text Color", "Selected Text"));
		for (int index = 0; index < TerminalThemeDraft.AnsiCount; index++)
		{
			draft.SetAnsi(index, Color(entries, SlotNames[index]));
		}

		return draft.Build(fileName);
	}

	public static string Write(TerminalTheme theme)
	{
		string[] colors =
		[
			theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
			theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
		];

		StringBuilder builder = new("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
		XmlWriterSettings settings = new()
		{
			Indent = true,
			IndentChars = "\t",
			NewLineChars = "\n",
			OmitXmlDeclaration = true,
		};

		using (StringWriter output = new(builder, CultureInfo.InvariantCulture))
		using (XmlWriter writer = XmlWriter.Create(output, settings))
		{
			writer.WriteDocType("plist", PublicId, SystemId, null);
			writer.WriteStartElement("plist");
			writer.WriteAttributeString("version", "1.0");
			writer.WriteStartElement("dict");
			for (int index = 0; index < colors.Length; index++)
			{
				WriteColor(writer, SlotNames[index], colors[index]);
			}

			WriteColor(writer, "Background Color", theme.Background);
			WriteColor(writer, "Cursor Color", theme.Cursor);
			WriteColor(writer, "Cursor Text Color", theme.CursorAccent);
			WriteColor(writer, "Foreground Color", theme.Foreground);
			WriteColor(writer, "Selection Color", theme.SelectionBackground);
			if (theme.SelectionForeground is { } selectionForeground)
			{
				WriteColor(writer, "Selected Text Color", selectionForeground);
			}

			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		builder.Append('\n');
		return builder.ToString();
	}

	private static Dictionary<string, XElement>? ReadRootDictionary(string text)
	{
		XElement? root;
		try
		{
			XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
			if (!IsShallow(text, settings))
			{
				return null;
			}

			using StringReader input = new(text);
			using XmlReader reader = XmlReader.Create(input, settings);
			root = XDocument.Load(reader).Root;
		}
		catch (XmlException)
		{
			return null;
		}

		XElement? dictionary = root?.Name.LocalName == "dict" ? root : root?.Elements().FirstOrDefault(child => child.Name.LocalName == "dict");
		return dictionary is null ? null : Entries(dictionary);
	}

	/// <summary>
	/// True when no element sits deeper than a color plist ever needs. Reading an element's text walks its children
	/// recursively, so a pasted document nested a hundred thousand levels deep would end the process with a stack
	/// overflow, which nothing can catch. The reader itself keeps its own stack, so this pass is safe at any depth.
	/// </summary>
	private static bool IsShallow(string text, XmlReaderSettings settings)
	{
		using StringReader input = new(text);
		using XmlReader reader = XmlReader.Create(input, settings);
		while (reader.Read())
		{
			if (reader.Depth > MaxDepth)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>A plist dictionary is a flat run of key elements each followed by its value element.</summary>
	private static Dictionary<string, XElement> Entries(XElement dictionary)
	{
		Dictionary<string, XElement> entries = new(StringComparer.OrdinalIgnoreCase);
		string? key = null;
		foreach (XElement child in dictionary.Elements())
		{
			if (child.Name.LocalName == "key")
			{
				key = child.Value.Trim();
			}
			else if (key is not null)
			{
				entries[key] = child;
				key = null;
			}
		}

		return entries;
	}

	private static string? Color(Dictionary<string, XElement> entries, params string[] names)
	{
		foreach (string name in names)
		{
			if (entries.TryGetValue(name, out XElement? element) && element.Name.LocalName == "dict" && Components(element) is { } color)
			{
				return color;
			}
		}

		return null;
	}

	private static string? Components(XElement element)
	{
		Dictionary<string, XElement> parts = Entries(element);
		if (Component(parts, "Red Component") is not { } red
			|| Component(parts, "Green Component") is not { } green
			|| Component(parts, "Blue Component") is not { } blue)
		{
			return null;
		}

		byte alpha = Component(parts, "Alpha Component") is { } value ? Byte(value) : (byte)255;
		return TerminalColors.FromChannels(Byte(red), Byte(green), Byte(blue), alpha);
	}

	private static double? Component(Dictionary<string, XElement> parts, string name) =>
		parts.TryGetValue(name, out XElement? element)
			&& double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
			&& double.IsFinite(value)
				? value
				: null;

	private static byte Byte(double component) => (byte)Math.Round(Math.Clamp(component, 0, 1) * 255);

	private static void WriteColor(XmlWriter writer, string key, string color)
	{
		(byte red, byte green, byte blue, byte alpha) = TerminalColors.Channels(color);
		writer.WriteElementString("key", key);
		writer.WriteStartElement("dict");

		// Apple writes the components in alphabetical order; keeping that makes exports diff against real files cleanly.
		WriteComponent(writer, "Alpha Component", alpha);
		WriteComponent(writer, "Blue Component", blue);
		writer.WriteElementString("key", "Color Space");
		writer.WriteElementString("string", ColorSpace);
		WriteComponent(writer, "Green Component", green);
		WriteComponent(writer, "Red Component", red);
		writer.WriteEndElement();
	}

	private static void WriteComponent(XmlWriter writer, string key, byte channel)
	{
		writer.WriteElementString("key", key);
		writer.WriteElementString("real", (channel / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
	}
}
