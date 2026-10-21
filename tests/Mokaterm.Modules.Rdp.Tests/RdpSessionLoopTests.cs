using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Sessions;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// The session loop over a fake connection and a fake IronRDP: how a session ends, what reaches the page and in which
/// order, and that a page or a server that misbehaves cannot freeze the rest.
/// </summary>
public sealed class RdpSessionLoopTests
{
	private const int Width = 64;
	private const int Height = 48;

	[Fact]
	public async Task ServerSignsTheUserOut_EndsTheSessionCleanlyWithTheReason()
	{
		await using Harness harness = Harness.Start();

		// What Windows sends when the user signs out (MS-RDPBCGR 1.3.1.4.2): the reason, then the ultimatum.
		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser));
		await harness.Wire.SendAsync(RdpPdus.Ultimatum(3));

		await Eventually.CompletesAsync(harness.Loop.Completion);
		Assert.Equal("The remote session ended because the user signed out.", harness.Loop.CloseReason);
		Assert.Empty(harness.Stage.Processed);
		await Eventually.CompletesAsync(harness.Wire.Closed);
	}

	[Fact]
	public async Task AnotherUserConnects_SaysSoWhenTheSessionEnds()
	{
		await using Harness harness = Harness.Start();

		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(RdpErrorInfo.DisconnectedByOtherConnection));

		await Eventually.CompletesAsync(harness.Loop.Completion);
		Assert.Equal("The remote session was ended because another user connected.", harness.Loop.CloseReason);
	}

	[Theory]
	[InlineData(RdpErrorInfo.RpcInitiatedDisconnect)]
	[InlineData(RdpErrorInfo.RpcInitiatedLogoff)]
	[InlineData(RdpErrorInfo.IdleTimeout)]
	[InlineData(RdpErrorInfo.RpcInitiatedDisconnectByUser)]
	[InlineData(RdpErrorInfo.ServerDwmCrash)]
	[InlineData(0x0000_10D0u)]
	[InlineData(0x0BAD_C0DEu)]
	public async Task ServerEndsTheSessionWithACode_CompletesWithItsWords(uint code)
	{
		await using Harness harness = Harness.Start();

		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(code));

		await Eventually.CompletesAsync(harness.Loop.Completion);
		Assert.Equal(RdpErrorInfo.Describe(code), harness.Loop.CloseReason);
	}

	[Fact]
	public async Task AnUltimatumWithNothingBeforeIt_EndsTheSessionCleanly()
	{
		await using Harness harness = Harness.Start();

		await harness.Wire.SendAsync(RdpPdus.Ultimatum(1));

		await Eventually.CompletesAsync(harness.Loop.Completion);
		Assert.Equal(RdpServerEnd.FromUltimatum(1).Reason, harness.Loop.CloseReason);
		Assert.Empty(harness.Stage.Processed);
	}

	[Fact]
	public async Task TheGraphicsSystemFailing_FaultsTheSessionSoItReconnects()
	{
		await using Harness harness = Harness.Start();

		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(RdpErrorInfo.GraphicsSubsystemFailed));

		RdpSessionException error = await Assert.ThrowsAsync<RdpSessionException>(() => Eventually.CompletesAsync(harness.Loop.Completion));
		Assert.Equal(RdpErrorInfo.Describe(RdpErrorInfo.GraphicsSubsystemFailed), error.Message);
		Assert.Null(harness.Loop.CloseReason);
	}

	[Fact]
	public async Task ServerHangsUpWithoutAWord_FaultsTheSessionSoItReconnects()
	{
		await using Harness harness = Harness.Start();

		harness.Wire.HangUp();

		RdpSessionException error = await Assert.ThrowsAsync<RdpSessionException>(() => Eventually.CompletesAsync(harness.Loop.Completion));
		Assert.Equal("The server closed the connection without ending the session first.", error.Message);
		Assert.IsType<EndOfStreamException>(error.InnerException);
		Assert.Null(harness.Loop.CloseReason);
	}

	[Fact]
	public async Task AnErrorCodeOfZero_IsLeftToIronRdpAndTheSessionGoesOn()
	{
		await using Harness harness = Harness.Start();

		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(RdpErrorInfo.None));

		await Eventually.TrueAsync(() => harness.Stage.Processed.Count == 1, "the stage got the message");
		await Eventually.StaysTrueAsync(() => !harness.Loop.Completion.IsCompleted, "the session runs on");
	}

	[Fact]
	public async Task AnErrorCodeOnAVirtualChannel_IsNotTheServerEndingTheSession()
	{
		await using Harness harness = Harness.Start();

		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser, channel: FakeStage.DefaultIoChannel + 1));

		await Eventually.TrueAsync(() => harness.Stage.Processed.Count == 1, "the stage got the message");
		await Eventually.StaysTrueAsync(() => !harness.Loop.Completion.IsCompleted, "the session runs on");
	}

	[Fact]
	public async Task WhatWindowsSaysAboutSigningIn_IsKeptAndRaisedAndStillGoesToIronRdp()
	{
		await using Harness harness = Harness.Start();
		ConcurrentQueue<RdpLogonNotice> raised = new();
		harness.Loop.LogonNoticed += raised.Enqueue;

		await harness.Wire.SendAsync(RdpPdus.LogonErrors(0xFFFF_FFFE, 2));
		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo(1, new byte[576]));

		await Eventually.TrueAsync(() => harness.Stage.Processed.Count == 2, "the stage got both messages");
		Assert.Equal<RdpLogonNoticeKind>([RdpLogonNoticeKind.Continuing, RdpLogonNoticeKind.SignedIn], [.. raised.Select(notice => notice.Kind)]);
		Assert.Equal(RdpLogonNoticeKind.SignedIn, harness.Loop.Logon?.Kind);
		Assert.False(harness.Loop.Completion.IsCompleted);
	}

	[Fact]
	public async Task IronRdpEndingTheSession_CompletesItCleanly()
	{
		await using Harness harness = Harness.Start();
		harness.Stage.OnProcess = _ => [RdpStageOutput.Response([0x01]), RdpStageOutput.Terminate];

		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());

		await Eventually.CompletesAsync(harness.Loop.Completion);
		Assert.Equal(RdpServerEnd.Terminated.Reason, harness.Loop.CloseReason);

		// The frame IronRDP asked for before it ended goes out first, as a Shutdown Denied's ultimatum must.
		Assert.Contains(harness.Wire.Sent, sent => sent.SequenceEqual(new byte[] { 0x01 }));
	}

	[Fact]
	public async Task ServerEndsTheSessionDuringAReactivation_EndsItCleanly()
	{
		await using Harness harness = Harness.Start();
		FakeActivation activation = new(steps: 2, 800, 600);
		harness.Stage.OnProcess = _ => [RdpStageOutput.Deactivate(activation)];

		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());
		await harness.Wire.SendAsync(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser));

		await Eventually.CompletesAsync(harness.Loop.Completion);
		Assert.Equal(RdpErrorInfo.Describe(RdpErrorInfo.LogoffByUser), harness.Loop.CloseReason);
		Assert.Empty(activation.Inputs);
		Assert.True(activation.Disposed);
	}

	[Fact]
	public async Task AnAttachedPage_LearnsTheSizeAndThenGetsTheWholeDesktopOpaque()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink sink = new();

		harness.Loop.Attach(sink);
		harness.Loop.Repaint();

		await Eventually.TrueAsync(() => !sink.Frames.IsEmpty, "the first frame arrived");
		string[] expected = ["resize 64x48", "frame 0,0 64x48"];
		Assert.Equal(expected, sink.Calls.Take(2));
		Assert.True(sink.Frames.TryPeek(out byte[]? frame));
		Assert.Equal(RdpFrameEncoder.RawFormat, RecordingSink.Field(frame, 0));
		Assert.Equal(RdpHarness.Gradient(Width, Height), frame[RdpFrameEncoder.HeaderLength..]);
	}

	[Fact]
	public async Task AReactivation_TellsThePageTheNewSizeBeforeAnyFrameOfIt()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink sink = new();
		harness.Loop.Attach(sink);
		harness.Loop.Repaint();
		await Eventually.TrueAsync(() => !sink.Frames.IsEmpty, "the first frame arrived");

		FakeActivation activation = new(steps: 1, 80, 60);
		harness.Stage.OnProcess = _ => [RdpStageOutput.Deactivate(activation)];
		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());
		byte[] demandActive = RdpPdus.SaveSessionInfo();
		await harness.Wire.SendAsync(demandActive);

		await Eventually.TrueAsync(() => sink.Calls.Contains("frame 0,0 80x60"), "the new desktop was painted");
		List<string> calls = [.. sink.Calls];
		Assert.True(calls.IndexOf("resize 80x60") < calls.IndexOf("frame 0,0 80x60"));
		Assert.Equal(demandActive, Assert.Single(activation.Inputs));
		Assert.Contains(harness.Wire.Sent, sent => sent.SequenceEqual(FakeActivation.Reply));
		Assert.Equal((80, 60), Assert.Single(harness.Stage.Resets));
		Assert.Equal(80, harness.Loop.Width);
		Assert.True(activation.Disposed);
	}

	[Fact]
	public async Task ASlowPage_HoldsUpNeitherInputNorTheWire()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink sink = new();
		TaskCompletionSource pageBusy = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		sink.Before = async (call, token) =>
		{
			pageBusy.TrySetResult();
			await release.Task.WaitAsync(token);
		};

		harness.Stage.OnProcess = _ => [RdpStageOutput.PointerImage(Pointer()), RdpStageOutput.PointerMoved(5, 6)];
		harness.Loop.Attach(sink);
		harness.Loop.Repaint();
		await Eventually.CompletesAsync(pageBusy.Task);

		// The page is stuck inside a call, and everything that goes to the server or comes from it still moves.
		await Eventually.CompletesAsync(harness.Loop.SendInputAsync([RdpInputEvent.KeyDown("KeyA")], TestContext.Current.CancellationToken).AsTask());
		Task<bool> resizing = harness.Loop.ResizeAsync(640, 480, TestContext.Current.CancellationToken).AsTask();
		await Eventually.CompletesAsync(resizing);
		Assert.True(await resizing);
		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());
		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());
		await Eventually.TrueAsync(() => harness.Stage.Processed.Count == 2, "both messages were read while the page was stuck");

		Assert.Equal(new RdpOperation(RdpOperationKind.KeyDown, 0x1E), Assert.Single(harness.Stage.Inputs));
		Assert.Contains(harness.Wire.Sent, sent => sent.SequenceEqual(FakeStage.ResizeReply));
		release.TrySetResult();

		// The page only ever gets the pointer the server drew last, however many it drew while the page was busy.
		await Eventually.TrueAsync(() => sink.Pointers.Count == 1 && sink.PointerMoves.Count == 1, "the pointer reached the page");
		await Eventually.StaysTrueAsync(() => sink.Pointers.Count == 1, "the pointer went once");
		Assert.Equal(0, harness.Stage.Violations);
	}

	[Fact]
	public async Task APageThatGoesAwayMidFrame_IsDetachedAndTheNextOneIsPainted()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink gone = new();
		TaskCompletionSource threw = new(TaskCreationOptions.RunContinuationsAsynchronously);
		gone.Before = (call, _) =>
		{
			threw.TrySetResult();
			throw new JSDisconnectedException("The circuit went away.");
		};

		harness.Loop.Attach(gone);
		harness.Loop.Repaint();
		await Eventually.CompletesAsync(threw.Task);
		await Eventually.StaysTrueAsync(() => !harness.Loop.Completion.IsCompleted, "the session runs on");

		using RecordingSink next = new();
		harness.Loop.Attach(next);
		harness.Loop.Repaint();
		await Eventually.TrueAsync(() => !next.Frames.IsEmpty, "the next page was painted");
		Assert.Empty(gone.Frames);
	}

	[Fact]
	public async Task APageThatBreaks_FaultsTheSessionInsteadOfFreezingIt()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink broken = new();
		broken.Before = (_, _) => throw new InvalidOperationException("A bug in the page.");

		harness.Loop.Attach(broken);
		harness.Loop.Repaint();

		RdpSessionException error = await Assert.ThrowsAsync<RdpSessionException>(() => Eventually.CompletesAsync(harness.Loop.Completion));
		Assert.Equal("The remote desktop could not be drawn any more, so the session was closed.", error.Message);
		Assert.IsType<InvalidOperationException>(error.InnerException);
		await Eventually.CompletesAsync(harness.Wire.Closed);
	}

	[Fact]
	public async Task ADesktopThatCannotBeCopied_FaultsTheSession()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink sink = new();
		harness.Stage.CopyFailure = new InvalidOperationException("The decoder broke.");

		harness.Loop.Attach(sink);
		harness.Loop.Repaint();

		RdpSessionException error = await Assert.ThrowsAsync<RdpSessionException>(() => Eventually.CompletesAsync(harness.Loop.Completion));
		Assert.Same(harness.Stage.CopyFailure, error.InnerException);
		Assert.Empty(sink.Frames);
	}

	[Fact]
	public async Task APointerTheServerDrew_ReachesThePageAsAPng()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink sink = new();
		harness.Loop.Attach(sink);
		RdpPointerPixels pointer = Pointer();
		harness.Stage.OnProcess = _ => [RdpStageOutput.PointerImage(pointer)];

		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());

		await Eventually.TrueAsync(() => !sink.Pointers.IsEmpty, "the pointer arrived");
		Assert.True(sink.Pointers.TryPeek(out RdpPointerImage? cursor));
		Assert.Equal((2, 2, 1, 0), (cursor.Width, cursor.Height, cursor.HotspotX, cursor.HotspotY));
		Assert.Equal(pointer.Rgba, PngReader.Read(cursor.Png).Pixels);
	}

	[Fact]
	public async Task PointerChangesInOneMessage_LeaveThePageWithTheLastOne()
	{
		await using Harness harness = Harness.Start();
		using RecordingSink sink = new();
		harness.Loop.Attach(sink);
		harness.Stage.OnProcess = _ => [RdpStageOutput.PointerImage(Pointer()), RdpStageOutput.Hidden];

		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());

		await Eventually.TrueAsync(() => !sink.PointerStyles.IsEmpty, "the pointer style arrived");
		Assert.True(Assert.Single(sink.PointerStyles));
		Assert.Empty(sink.Pointers);
	}

	[Fact]
	public async Task APageThatAttachesLater_GetsThePointerTheServerDrewButNotWhereItMovedIt()
	{
		await using Harness harness = Harness.Start();
		harness.Stage.OnProcess = _ => [RdpStageOutput.PointerImage(Pointer()), RdpStageOutput.PointerMoved(7, 8)];
		using RecordingSink first = new();
		harness.Loop.Attach(first);
		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());
		await Eventually.TrueAsync(() => !first.Pointers.IsEmpty && !first.PointerMoves.IsEmpty, "the first page has the pointer");

		using RecordingSink second = new();
		harness.Loop.Attach(second);

		await Eventually.TrueAsync(() => !second.Pointers.IsEmpty, "the second page has the pointer");
		await Eventually.StaysTrueAsync(() => second.PointerMoves.IsEmpty, "an old server move is not replayed");
	}

	[Fact]
	public async Task WhereTheServerPutThePointer_MakesTheSameMoveFromThePageRedundant()
	{
		await using Harness harness = Harness.Start();
		harness.Stage.OnProcess = _ => [RdpStageOutput.PointerMoved(10, 20)];
		await harness.Wire.SendAsync(RdpPdus.SaveSessionInfo());
		await Eventually.TrueAsync(() => harness.Stage.Processed.Count == 1, "the move was read");

		await Eventually.CompletesAsync(harness.Loop.SendInputAsync([RdpInputEvent.MouseMove(10, 20)], TestContext.Current.CancellationToken).AsTask());

		Assert.Empty(harness.Stage.Inputs);
	}

	[Fact]
	public async Task Dispose_FreesTheStageOnlyOnceNothingIsInsideIt()
	{
		Harness harness = Harness.Start();
		using RecordingSink sink = new();
		harness.Loop.Attach(sink);
		harness.Loop.Repaint();
		Task typing = Task.Run(async () =>
		{
			try
			{
				while (!harness.Loop.Completion.IsCompleted)
				{
					await harness.Loop.SendInputAsync([RdpInputEvent.KeyDown("KeyB"), RdpInputEvent.KeyUp("KeyB")], TestContext.Current.CancellationToken);
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
			{
				// What the view also sees when the session closes under a keystroke.
			}
		}, TestContext.Current.CancellationToken);

		await Eventually.TrueAsync(() => !harness.Stage.Inputs.IsEmpty, "input reached the stage");
		await harness.DisposeAsync();

		Assert.True(harness.Stage.Disposed);
		Assert.Equal(0, harness.Stage.Violations);
		await Eventually.CompletesAsync(harness.Loop.Completion);
		await Eventually.CompletesAsync(typing);
	}

	/// <summary>A 2 x 2 pointer, its pixels all different so a wrong row or channel shows up.</summary>
	private static RdpPointerPixels Pointer() => new(
		[1, 2, 3, 255, 4, 5, 6, 128, 7, 8, 9, 0, 10, 11, 12, 64],
		Width: 2,
		Height: 2,
		HotspotX: 1,
		HotspotY: 0);

	private sealed class Harness : IAsyncDisposable
	{
		private Harness(FakeWire wire, FakeStage stage, RdpSessionLoop loop)
		{
			Wire = wire;
			Stage = stage;
			Loop = loop;
		}

		public FakeWire Wire { get; }

		public FakeStage Stage { get; }

		public RdpSessionLoop Loop { get; }

		public static Harness Start()
		{
			FakeWire wire = new();
			FakeStage stage = new(Width, Height);
			RdpSettings settings = new() { FramesPerSecond = RdpSettings.MaxFramesPerSecond, RawFrameKilobytes = RdpSettings.MaxRawFrameKilobytes };
			RdpSessionLoop loop = new(wire, stage, Width, Height, settings, NullLogger.Instance);
			loop.Start();
			return new Harness(wire, stage, loop);
		}

		public async ValueTask DisposeAsync()
		{
			await Eventually.CompletesAsync(Loop.DisposeAsync().AsTask());
			Wire.Dispose();
		}
	}
}
