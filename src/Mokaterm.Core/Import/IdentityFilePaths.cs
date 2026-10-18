using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Import;

namespace Mokaterm.Core.Import;

/// <summary>
/// Turns the key path an imported login names into a full path on this machine, the way the client that wrote it reads
/// it. OpenSSH expands <c>~</c> first, then its <c>%</c> tokens and <c>${NAME}</c> variables, and this takes a relative
/// path from <c>~/.ssh</c>. PuTTY and WinSCP keep a full Windows path, in which <c>%NAME%</c> variables are expanded.
/// Everything about this machine comes in through <see cref="IdentityPathContext"/>, so nothing here touches the disk.
/// </summary>
internal static class IdentityFilePaths
{
	private const int DefaultSshPort = 22;

	public static IdentityPathResult Resolve(string raw, IdentityPathStyle style, IdentityPathContext context)
	{
		ArgumentNullException.ThrowIfNull(raw);
		ArgumentNullException.ThrowIfNull(context);
		string path = Unquote(raw.Trim());
		if (path.Length == 0)
		{
			return IdentityPathResult.Problem("The login names an empty key path.");
		}

		return style == IdentityPathStyle.OpenSsh ? ResolveOpenSsh(path, context) : ResolvePlain(path, context);
	}

	private static IdentityPathResult ResolveOpenSsh(string path, IdentityPathContext context)
	{
		if (path[0] == '~')
		{
			int end = path.AsSpan(1).IndexOfAny('/', '\\');
			string user = end < 0 ? path[1..] : path[1..(end + 1)];
			if (user.Length > 0 && !user.Equals(context.LocalUser, StringComparison.OrdinalIgnoreCase))
			{
				return IdentityPathResult.Problem($"The key path starts with ~{user}, the home folder of another account, which the import does not look up.");
			}

			path = context.HomeDirectory + (end < 0 ? "" : path[(end + 1)..]);
		}

		StringBuilder expanded = new(path.Length + 32);
		for (int index = 0; index < path.Length; index++)
		{
			char character = path[index];
			if (character == '%')
			{
				if (index + 1 >= path.Length)
				{
					return IdentityPathResult.Problem("The key path ends in a lone %.");
				}

				char token = path[++index];
				string? value = token switch
				{
					'%' => "%",
					'd' => context.HomeDirectory,
					'u' => context.LocalUser,
					'h' => context.HostName,
					'n' => context.Alias,
					'r' => string.IsNullOrEmpty(context.RemoteUser) ? context.LocalUser : context.RemoteUser,
					'p' => (context.Port ?? DefaultSshPort).ToString(CultureInfo.InvariantCulture),
					'l' => context.LocalHost,
					'L' => ShortName(context.LocalHost),
					_ => null,
				};

				if (value is null)
				{
					return IdentityPathResult.Problem($"The key path uses %{token}, which the import cannot fill in.");
				}

				expanded.Append(value);
				continue;
			}

			if (character == '$' && index + 1 < path.Length && path[index + 1] == '{')
			{
				int close = path.IndexOf('}', index + 2);
				if (close < 0)
				{
					return IdentityPathResult.Problem("The key path has a ${ without its closing brace.");
				}

				string name = path[(index + 2)..close];
				if (name.Length == 0 || context.Variable(name) is not { } variable)
				{
					return IdentityPathResult.Problem($"The key path names the environment variable {name}, which is not set here.");
				}

				expanded.Append(variable);
				index = close;
				continue;
			}

			expanded.Append(character);
		}

		string result = expanded.ToString();
		bool relative = !Path.IsPathRooted(result);
		return Full(relative ? Path.Combine(context.HomeDirectory, ".ssh", result) : result, relative);
	}

	private static IdentityPathResult ResolvePlain(string path, IdentityPathContext context)
	{
		StringBuilder expanded = new(path.Length + 32);
		int index = 0;
		while (index < path.Length)
		{
			int open = path.IndexOf('%', index);
			int close = open < 0 ? -1 : path.IndexOf('%', open + 1);
			if (close < 0)
			{
				expanded.Append(path, index, path.Length - index);
				break;
			}

			expanded.Append(path, index, open - index);
			string name = path[(open + 1)..close];

			// Windows leaves a name it does not know as it was written, percent signs and all.
			if (name.Length > 0 && context.Variable(name) is { } value)
			{
				expanded.Append(value);
				index = close + 1;
			}
			else
			{
				expanded.Append('%');
				index = open + 1;
			}
		}

		string result = Unquote(expanded.ToString());
		return Path.IsPathFullyQualified(result)
			? Full(result, relative: false)
			: IdentityPathResult.Problem("The key path is not a full path, so there is no telling which folder it means.");
	}

	private static IdentityPathResult Full(string path, bool relative)
	{
		try
		{
			return new IdentityPathResult(Path.GetFullPath(path), null, relative);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return IdentityPathResult.Problem("The key path is not a valid path on this machine.");
		}
	}

	private static string Unquote(string path) =>
		path.Length >= 2 && path[0] == '"' && path[^1] == '"' ? path[1..^1] : path;

	private static string ShortName(string hostName)
	{
		int dot = hostName.IndexOf('.', StringComparison.Ordinal);
		return dot < 0 ? hostName : hostName[..dot];
	}
}
