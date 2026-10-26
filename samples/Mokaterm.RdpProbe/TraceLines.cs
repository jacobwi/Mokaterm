namespace Mokaterm.RdpProbe;

internal sealed class TraceLines(ProbeOutput output) : IProgress<string>
{
	public void Report(string value) => output.Trace(value);
}
