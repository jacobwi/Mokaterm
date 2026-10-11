using System.Diagnostics;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// The machine's own <c>ssh-keygen</c>, for checking security key files and signatures against OpenSSH's code rather
/// than against ours: <c>-y</c> parses a key file the way <c>ssh</c> does, and <c>-Y check-novalidate</c> verifies a
/// signature with the same <c>sshkey_verify</c> sshd calls for a login. Tests skip when there is none.
/// </summary>
internal static class OpenSshKeygen
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

	private static readonly string[] Candidates =
	[
		@"C:\Program Files\Git\usr\bin\ssh-keygen.exe",
		@"C:\Windows\System32\OpenSSH\ssh-keygen.exe",
		"/usr/bin/ssh-keygen",
		"/usr/local/bin/ssh-keygen",
	];

	public static string? Find() => Candidates.FirstOrDefault(File.Exists);

	/// <summary>
	/// Runs it with <paramref name="input"/> on stdin, which is then closed, so a prompt it should not show ends the run
	/// instead of waiting for an answer.
	/// </summary>
	public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string program, IEnumerable<string> arguments, byte[]? input, CancellationToken cancellationToken)
	{
		ProcessStartInfo start = new(program)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(start) ?? throw new InvalidOperationException("ssh-keygen did not start.");
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		if (input is not null)
		{
			await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken);
		}

		process.StandardInput.Close();
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(Limit);
		try
		{
			await process.WaitForExitAsync(limit.Token);
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			throw;
		}

		return (process.ExitCode, await output, await error);
	}
}
