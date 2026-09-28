using System.Globalization;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>Parses the output of <see cref="RemoteScripts.List"/> into entries.</summary>
internal static class RemoteListingParser
{
	private const int GnuFieldCount = 9;
	private const int PosixFieldCount = 4;
	private const string PosixUnknownName = "UNKNOWN";

	/// <summary>
	/// Parses a listing of <paramref name="path"/>, or the single entry for <paramref name="path"/> itself when
	/// <paramref name="isStat"/>. <paramref name="payload"/> starts after the output marker. Malformed records are skipped.
	/// </summary>
	public static List<RemoteFileEntry> Parse(ReadOnlySpan<byte> payload, string path, bool isStat)
	{
		List<string> fields = RemoteOutput.SplitFields(payload);
		List<RemoteFileEntry> entries = [];
		if (fields.Count == 0)
		{
			return entries;
		}

		string normalizedPath = RemotePath.Normalize(path);
		int recordLength = fields[0] switch
		{
			"G" => GnuFieldCount,
			"P" => PosixFieldCount,
			_ => 0,
		};

		if (recordLength == 0)
		{
			return entries;
		}

		for (int start = 1; start + recordLength <= fields.Count; start += recordLength)
		{
			List<string> record = fields.GetRange(start, recordLength);
			RemoteFileEntry? entry = recordLength == GnuFieldCount
				? FromGnu(record, normalizedPath, isStat)
				: FromPosix(record, normalizedPath, isStat);

			if (entry is not null)
			{
				entries.Add(entry);
			}
		}

		return entries;
	}

	// type, size, mtime, mode, user, group, link target, target type, name
	private static RemoteFileEntry? FromGnu(List<string> record, string path, bool isStat)
	{
		if (!TryCreateLocation(record[8], path, isStat, out string name, out string entryPath)
			|| !long.TryParse(record[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size)
			|| !TryParseOctal(record[3], out int modeBits))
		{
			return null;
		}

		RemoteEntryKind kind = record[0] switch
		{
			"f" => RemoteEntryKind.File,
			"d" => RemoteEntryKind.Directory,
			"l" => RemoteEntryKind.SymbolicLink,
			_ => RemoteEntryKind.Other,
		};

		bool isLink = kind == RemoteEntryKind.SymbolicLink;
		return new RemoteFileEntry
		{
			Name = name,
			Path = entryPath,
			Kind = kind,
			Size = size,
			LastModified = ParseEpoch(record[2]),
			Permissions = UnixModes.FromBits(modeBits),
			Owner = NullIfEmpty(record[4]),
			Group = NullIfEmpty(record[5]),
			LinkTarget = isLink ? NullIfEmpty(record[6]) : null,
			LinkTargetKind = isLink ? ParseTargetKind(record[7]) : null,
		};
	}

	// "rawmode(hex) size mtime uid gid user group", link target, target type, name
	private static RemoteFileEntry? FromPosix(List<string> record, string path, bool isStat)
	{
		string[] info = record[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (info.Length < 7
			|| !TryCreateLocation(record[3], path, isStat, out string name, out string entryPath)
			|| !int.TryParse(info[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rawMode)
			|| !long.TryParse(info[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size)
			|| !long.TryParse(info[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long seconds))
		{
			return null;
		}

		RemoteEntryKind kind = UnixModes.KindFromBits(rawMode);
		bool isLink = kind == RemoteEntryKind.SymbolicLink;
		return new RemoteFileEntry
		{
			Name = name,
			Path = entryPath,
			Kind = kind,
			Size = size,
			LastModified = DateTimeOffset.FromUnixTimeSeconds(seconds),
			Permissions = UnixModes.FromBits(rawMode),
			Owner = info[5] == PosixUnknownName ? info[3] : info[5],
			Group = info[6] == PosixUnknownName ? info[4] : info[6],
			LinkTarget = isLink ? NullIfEmpty(record[1]) : null,
			LinkTargetKind = isLink ? ParseTargetKind(record[2]) : null,
		};
	}

	private static bool TryCreateLocation(string listedName, string path, bool isStat, out string name, out string entryPath)
	{
		if (isStat)
		{
			entryPath = path;
			name = RemotePath.GetName(path);
			return true;
		}

		name = listedName;
		entryPath = RemotePath.Combine(path, listedName);
		return RemotePath.IsValidName(listedName);
	}

	/// <summary>Target type letters from <c>find %Y</c> and the POSIX fallback. Loops and missing targets have no kind.</summary>
	private static RemoteEntryKind? ParseTargetKind(string value) => value switch
	{
		"d" => RemoteEntryKind.Directory,
		"f" => RemoteEntryKind.File,
		"" or "N" or "L" or "?" or "l" => null,
		_ => RemoteEntryKind.Other,
	};

	/// <summary><c>find %T@</c>: seconds since the epoch with an optional fraction, such as <c>1694959123.2512345670</c>.</summary>
	internal static DateTimeOffset? ParseEpoch(string value)
	{
		int dot = value.IndexOf('.', StringComparison.Ordinal);
		ReadOnlySpan<char> whole = dot < 0 ? value : value.AsSpan(0, dot);
		if (!long.TryParse(whole, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long seconds)
			|| seconds is <= -62_135_596_800 or >= 253_402_300_799)
		{
			return null;
		}

		DateTimeOffset time = DateTimeOffset.FromUnixTimeSeconds(seconds);
		if (dot < 0)
		{
			return time;
		}

		ReadOnlySpan<char> fraction = value.AsSpan(dot + 1);
		long ticks = 0;
		int digits = 0;
		foreach (char c in fraction)
		{
			if (!char.IsAsciiDigit(c))
			{
				return time;
			}

			if (digits < 7)
			{
				ticks = (ticks * 10) + (c - '0');
				digits++;
			}
		}

		for (; digits < 7; digits++)
		{
			ticks *= 10;
		}

		return seconds < 0 ? time.AddTicks(-ticks) : time.AddTicks(ticks);
	}

	private static bool TryParseOctal(string value, out int bits)
	{
		bits = 0;
		if (value.Length is 0 or > 7)
		{
			return false;
		}

		foreach (char c in value)
		{
			if (c is < '0' or > '7')
			{
				return false;
			}

			bits = (bits * 8) + (c - '0');
		}

		return true;
	}

	private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
