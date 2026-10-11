using System.ComponentModel;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Pty;

namespace Mokaterm.Modules.Local;

/// <summary>Turns what Windows says when a program does not start into a sentence the connect error can show.</summary>
internal static class LocalStartErrors
{
	private const int FileNotFound = 2;
	private const int PathNotFound = 3;
	private const int AccessDenied = 5;
	private const int BadExeFormat = 193;
	private const int InvalidDirectory = 267;
	private const int ElevationRequired = 740;

	/// <param name="displayName">What the sentence calls the program, such as <c>PowerShell</c> or <c>docker exec</c>.</param>
	public static ProtocolConnectException Describe(Exception exception, string displayName, PseudoTerminalLaunch launch)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(displayName);
		ArgumentNullException.ThrowIfNull(launch);
		string message = exception switch
		{
			Win32Exception { NativeErrorCode: FileNotFound or PathNotFound } => $"{launch.Program} was not found.",
			Win32Exception { NativeErrorCode: AccessDenied } => $"Windows refused to start {launch.Program}.",
			Win32Exception { NativeErrorCode: BadExeFormat } => $"{launch.Program} is not a program Windows can run.",
			Win32Exception { NativeErrorCode: InvalidDirectory } => $"{launch.WorkingDirectory} cannot be used as the starting folder.",
			Win32Exception { NativeErrorCode: ElevationRequired } =>
				$"{displayName} asks to run as administrator, which a shell inside this app cannot do.",
			PlatformNotSupportedException => PseudoTerminalPlatform.Unsupported,
			_ => $"{displayName} could not be started: {exception.Message}",
		};

		return new ProtocolConnectException(ConnectFailure.HostUnreachable, message, exception);
	}
}
