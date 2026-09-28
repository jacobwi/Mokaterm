namespace Mokaterm.Tests.Shared;

internal static class TestTimeouts
{
	/// <summary>
	/// The <c>[Fact(Timeout = ...)]</c> a test that reaches a socket or a device carries: it fails instead of hanging
	/// the run, which would also hold the shared build lock.
	/// </summary>
	public const int NetworkMilliseconds = 60_000;
}
