using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

internal static class DockerExitCodes
{
	public static string Meaning(int code, bool oomKilled = false)
	{
		if (oomKilled)
		{
			return "killed when it ran out of memory";
		}

		return code switch
		{
			0 => "it ended without an error",
			1 => "general error: the program failed",
			2 => "the program reported an error (bad arguments, or a Go panic)",
			125 => "docker itself could not run the container",
			126 => "the command could not run: not executable, or no permission",
			127 => "the command was not found in the image",
			130 => "interrupted (SIGINT)",
			134 => "aborted (SIGABRT), often a failed assertion",
			137 => "killed (SIGKILL): out of memory, or killed from outside",
			139 => "segmentation fault (SIGSEGV): the program crashed",
			143 => "terminated (SIGTERM): it was asked to stop",
			> 128 and < 160 => string.Create(CultureInfo.InvariantCulture, $"ended by signal {code - 128}"),
			255 => "exit status out of range, often an exit(-1)",
			_ => "the program's own error code",
		};
	}

	public static string Describe(int code, bool oomKilled = false) =>
		string.Create(CultureInfo.InvariantCulture, $"{code}, {Meaning(code, oomKilled)}");
}
