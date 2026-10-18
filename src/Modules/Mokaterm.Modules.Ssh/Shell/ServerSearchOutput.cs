using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// Reads what <see cref="RemoteScripts.Search"/> printed after its marker. It takes the records in find's order and
/// stops exactly where the script's counter stopped find, judging every byte, folder and match the way that awk does, so
/// what it reports never depends on how far find got before the broken pipe reached it. Every entry is held to
/// <see cref="RemoteSearchQuery.Matches"/>, whatever find's own pattern matched, and to the caps the walk keeps.
/// </summary>
internal static class ServerSearchOutput
{
	/// <summary>Fields of a match record from a find with <c>-printf</c>: <see cref="RemoteScripts.List"/>'s eight, then the path.</summary>
	private const int GnuRecordFields = 9;

	/// <summary>Fields of a match record from the shell: the stat line, link target, target type, then the path.</summary>
	private const int PosixRecordFields = 4;

	/// <summary>The search's own folder, as find names it after the script changed into it.</summary>
	private const string Start = ".";

	/// <summary>
	/// The search's outcome, or null when the output is not the script's (no format, no closing counts), which says this
	/// server's tools print something else and the walk should take over for good.
	/// </summary>
	/// <param name="root">The absolute folder searched, which every path is placed under.</param>
	/// <param name="wholeSeconds">Cut times to whole seconds, as an SFTP listing reports them.</param>
	/// <param name="budget">The budget the script ran with, <see cref="ServerSearchBudget.Default"/> when null.</param>
	public static ServerSearchResult? Read(ReadOnlySpan<byte> payload, string root, RemoteSearchQuery query, bool wholeSeconds, ServerSearchBudget? budget = null)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(query);
		int maxBytes = (budget ?? ServerSearchBudget.Default).MaxRecordBytes;
		List<(int Start, int Length)> fields = Split(payload);
		if (fields.Count < 3)
		{
			return null;
		}

		SearchNamePatterns names = SearchNamePatterns.For(query);
		(int recordFields, int resultStop) = Text(payload, fields[0]) switch
		{
			"G" => (GnuRecordFields, ServerSearch.ResultStop(query, names.AlternativesExact)),
			"P" => (PosixRecordFields, ServerSearch.ResultStop(query, names.SingleExact)),
			_ => (0, 0),
		};

		if (recordFields == 0
			|| !TryReadCount(Text(payload, fields[^2]), 'U', out int unreadable)
			|| !TryReadStatus(Text(payload, fields[^1]), out int? status))
		{
			return null;
		}

		int end = fields.Count - 2;
		long bytes = 0;
		int counted = 0;
		int candidates = 0;
		int found = 0;
		bool full = false;
		bool resultsCut = false;
		bool leftOver = false;
		bool leftDeeper = false;
		bool bytesCut = false;
		HashSet<string> entered = new(StringComparer.Ordinal) { Start };
		List<ServerSearchStep> steps = [];
		int index = 1;
		while (index < end)
		{
			(int start, int length) = fields[index];
			if (length == 0)
			{
				// The empty field the script writes before its counts: nothing find printed is empty at a record's start.
				index++;
				continue;
			}

			byte tag = payload[start];
			if (tag is not ((byte)'D' or (byte)'M'))
			{
				break;
			}

			// The walk stops at the cap and says it was cut when anything was left to look at.
			if (full)
			{
				resultsCut = true;
				break;
			}

			if (!Count(ref bytes, length, maxBytes))
			{
				bytesCut = true;
				break;
			}

			if (tag == (byte)'D')
			{
				// A folder at the depth cap is not entered, and the awk does not count it. Names hold no slash, so the
				// slashes of ./a/b are its depth.
				string folder = Text(payload, (start + 1, length - 1));
				bool placed = Parent(folder) is { } parent && entered.Contains(parent);
				index++;
				if (payload.Slice(start, length).Count((byte)'/') >= query.MaxDepth)
				{
					leftDeeper |= placed;
					continue;
				}

				if (++counted >= query.MaxFolders)
				{
					leftOver = true;
					break;
				}

				if (placed && entered.Add(folder))
				{
					steps.Add(new ServerSearchStep(Place(root, folder), null));
				}

				continue;
			}

			if (resultStop > 0 && ++candidates > resultStop)
			{
				resultsCut = true;
				break;
			}

			int counting = 1;
			while (counting < recordFields && index + counting < end && Count(ref bytes, fields[index + counting].Length, maxBytes))
			{
				counting++;
			}

			if (counting < recordFields)
			{
				bytesCut = index + counting < end;
				break;
			}

			if (Entry(payload, fields, index, recordFields, root, entered, wholeSeconds) is { } entry && query.Matches(entry))
			{
				steps.Add(new ServerSearchStep(null, entry));
				full = ++found >= query.MaxResults;
			}

			index += recordFields;
		}

		// The last match the cap allowed was also the last record: cut only if the walk would have had more waiting.
		if (full && !resultsCut)
		{
			resultsCut = leftDeeper || leftOver;
		}

		int visited = 1 + steps.Count(step => step.Folder is not null);
		bool stopped = resultsCut || leftOver || bytesCut;
		return new ServerSearchResult
		{
			Steps = steps,
			FoldersVisited = visited,
			UnreadableFolders = Math.Min(unreadable, visited - 1),
			Limit = resultsCut ? RemoteSearchLimit.Results
				: leftOver ? RemoteSearchLimit.Folders
				: leftDeeper ? RemoteSearchLimit.Depth
				: RemoteSearchLimit.None,
			NeedsWalk = bytesCut && !resultsCut && !leftOver,
			TimedOut = !stopped && status is { } code && ShellTimeout.RanOut(code),
		};
	}

	/// <summary>The awk's own byte count: every field and its line break, and past the budget it stops.</summary>
	private static bool Count(ref long bytes, int length, int maxBytes)
	{
		bytes += length + 1;
		return bytes <= maxBytes;
	}

	private static RemoteFileEntry? Entry(
		ReadOnlySpan<byte> payload,
		List<(int Start, int Length)> fields,
		int index,
		int recordFields,
		string root,
		HashSet<string> entered,
		bool wholeSeconds)
	{
		string path = Text(payload, fields[index + recordFields - 1]);
		if (Parent(path) is not { } parent || !entered.Contains(parent))
		{
			return null;
		}

		string[] record = new string[recordFields - 1];
		(int start, int length) = fields[index];
		record[0] = Text(payload, (start + 1, length - 1));
		for (int field = 1; field < record.Length; field++)
		{
			record[field] = Text(payload, fields[index + field]);
		}

		string name = path[(path.LastIndexOf('/') + 1)..];
		string place = Place(root, path);
		RemoteFileEntry? entry = recordFields == GnuRecordFields
			? RemoteListingParser.FromGnuFields(record, name, place)
			: RemoteListingParser.FromPosixFields(record, name, place);

		return wholeSeconds && entry?.LastModified is { } modified
			? entry with { LastModified = modified.AddTicks(-(modified.Ticks % TimeSpan.TicksPerSecond)) }
			: entry;
	}

	/// <summary>
	/// The folder that holds <paramref name="path"/>, in find's <c>./</c> form, or null when the last part is no name find
	/// would print. The parts before it are checked by the parent having been entered, which only a checked path can be.
	/// </summary>
	private static string? Parent(string path)
	{
		if (!path.StartsWith("./", StringComparison.Ordinal))
		{
			return null;
		}

		int slash = path.LastIndexOf('/');
		return RemotePath.IsValidName(path[(slash + 1)..]) ? path[..slash] : null;
	}

	private static string Place(string root, string path) => RemotePath.Combine(root, path[2..]);

	private static List<(int Start, int Length)> Split(ReadOnlySpan<byte> payload)
	{
		List<(int Start, int Length)> fields = [];
		int start = 0;
		while (start < payload.Length)
		{
			int end = payload[start..].IndexOf((byte)0);
			if (end < 0)
			{
				// A field without its NUL was cut off, and a whole record never ends that way.
				break;
			}

			fields.Add((start, end));
			start += end + 1;
		}

		return fields;
	}

	private static string Text(ReadOnlySpan<byte> payload, (int Start, int Length) field) =>
		Encoding.UTF8.GetString(payload.Slice(field.Start, field.Length));

	private static bool TryReadCount(string field, char tag, out int count)
	{
		count = 0;
		return field.Length > 1 && field[0] == tag
			&& int.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out count);
	}

	// An empty status is a find whose subshell never got to print it; that is no time limit either.
	private static bool TryReadStatus(string field, out int? status)
	{
		status = null;
		if (field.Length == 0 || field[0] != 'E')
		{
			return false;
		}

		if (field.Length > 1 && int.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int code))
		{
			status = code;
		}

		return true;
	}
}
