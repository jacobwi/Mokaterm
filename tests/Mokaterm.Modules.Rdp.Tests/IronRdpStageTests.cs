using Mokaterm.Modules.Rdp.Sessions;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// The order outputs leave the bindings in. <c>ActiveStageOutputIterator.next</c> is <c>Vec::pop</c>, so the model here
/// pops from the end of a list the way the native one does.
/// </summary>
public sealed class IronRdpStageTests
{
	[Fact]
	public void Collect_PutsWhatTheIteratorPopsBackInTheOrderIronRdpReturnedIt()
	{
		List<Native> returned = [new(1), new(2), new(3)];
		List<RdpStageOutput> outputs = [];

		IronRdpStage.Collect(Pop(returned), native => RdpStageOutput.Response([native.Id]), outputs);

		int[] expected = [1, 2, 3];
		Assert.Equal(expected, outputs.Select(output => (int)output.Frame[0]));
	}

	[Fact]
	public void Collect_DisposesEveryNativeOutput()
	{
		Native[] natives = [new(1), new(2)];
		List<Native> returned = [.. natives];

		IronRdpStage.Collect(Pop(returned), native => RdpStageOutput.Response([native.Id]), []);

		Assert.All(natives, native => Assert.True(native.Disposed));
	}

	[Fact]
	public void Collect_LeavesOutWhatTheConversionIgnoresAndKeepsWhatWasThere()
	{
		RdpStageOutput earlier = RdpStageOutput.Response([9]);
		List<RdpStageOutput> outputs = [earlier];
		List<Native> returned = [new(1), new(2), new(3)];

		IronRdpStage.Collect(Pop(returned), native => native.Id == 2 ? null : RdpStageOutput.Response([native.Id]), outputs);

		int[] expected = [9, 1, 3];
		Assert.Equal(expected, outputs.Select(output => (int)output.Frame[0]));
	}

	[Fact]
	public void Collect_AFailingConversion_ReleasesWhatItMadeAndAddsNothing()
	{
		FakeActivation activation = new(1, 10, 10);
		RdpStageOutput earlier = RdpStageOutput.Response([9]);
		List<RdpStageOutput> outputs = [earlier];
		Native failing = new(1);
		List<Native> returned = [failing, new(2)];

		Assert.Throws<InvalidOperationException>(() => IronRdpStage.Collect(
			Pop(returned),
			native => native.Id == 2 ? RdpStageOutput.Deactivate(activation) : throw new InvalidOperationException("broken"),
			outputs));

		Assert.Same(earlier, Assert.Single(outputs));
		Assert.True(activation.Disposed);
		Assert.True(failing.Disposed);
	}

	private static Func<Native?> Pop(List<Native> returned) => () =>
	{
		if (returned.Count == 0)
		{
			return null;
		}

		Native last = returned[^1];
		returned.RemoveAt(returned.Count - 1);
		return last;
	};

	private sealed class Native(byte id) : IDisposable
	{
		public byte Id { get; } = id;

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}
}
