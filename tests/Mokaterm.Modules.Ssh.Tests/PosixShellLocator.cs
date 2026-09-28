using System.Diagnostics;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>Finds a local POSIX shell (Linux, macOS, or Git for Windows) to run the module's scripts against.</summary>
internal static class PosixShellLocator
{
	/// <summary>Git for Windows' sh inherits the Windows PATH, where <c>find</c> is the Windows FIND command.</summary>
	public const string UnixPathPrefix = "PATH=/usr/bin:/bin:$PATH\nexport PATH\n";

	private static readonly string[] Candidates =
	[
		"/bin/sh",
		@"C:\Program Files\Git\usr\bin\sh.exe",
		@"C:\Program Files\Git\bin\sh.exe",
	];

	public static string? Find() => Candidates.FirstOrDefault(File.Exists);

	/// <summary>
	/// Runs <paramref name="script"/> with <paramref name="arguments"/> as <c>$1</c> and on. The script goes in on stdin
	/// (<c>sh -s</c>) so no command-line quoting sits between the test and the shell.
	/// </summary>
	public static async Task<(int ExitCode, byte[] Output, string Error)> RunAsync(string shell, string script, IEnumerable<string> arguments)
	{
		ProcessStartInfo start = new(shell)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		start.ArgumentList.Add("-s");
		start.ArgumentList.Add("--");
		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(start) ?? throw new InvalidOperationException("sh did not start.");
		using MemoryStream output = new();
		Task copyOutput = process.StandardOutput.BaseStream.CopyToAsync(output);
		Task<string> readError = process.StandardError.ReadToEndAsync();

		byte[] input = System.Text.Encoding.UTF8.GetBytes(script.ReplaceLineEndings("\n"));
		await process.StandardInput.BaseStream.WriteAsync(input);
		process.StandardInput.Close();

		await copyOutput;
		string error = await readError;
		await process.WaitForExitAsync();
		return (process.ExitCode, output.ToArray(), error);
	}
}
