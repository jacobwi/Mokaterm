using System.Collections;
using System.Reflection;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Every place this module reaches inside SSH.NET 2026.0.0 without a public API, which agent and X11 forwarding and
/// keepalives that want a reply need and nothing else does. Each lookup runs once. One that fails turns off the feature
/// that needs it, with the reason, and never throws into a session; the tests for this type fail when an SSH.NET update
/// moves any of it.
/// </summary>
/// <remarks>
/// What is public: <see cref="Session"/> with its channel events and its global request reply events, the channel
/// messages and <see cref="RequestInfo"/>. What is not: <c>BaseClient.Session</c>, which is the only way from a client to
/// its session; <c>Session.SendMessage</c>, which is the only way to write a message; the message table, which has no
/// class for the <c>auth-agent@openssh.com</c> channel type and so fails the whole connection when a server opens one;
/// and the <see cref="GlobalRequestMessage"/> constructor that takes a request name, whose public one leaves the name
/// empty and then cannot write it.
/// </remarks>
internal static class SshNetInternals
{
	public const string ChannelOpenMessageName = "SSH_MSG_CHANNEL_OPEN";

	/// <summary>OpenSSH's keepalive, which every server answers: a server answers a request it does not know with a failure.</summary>
	public const string KeepAliveRequestName = "keepalive@openssh.com";

	private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

	private static readonly Lazy<SessionAccess> Access = new(LoadSessionAccess, LazyThreadSafetyMode.ExecutionAndPublication);

	private static readonly Lazy<KeepAliveAccess> KeepAlive = new(LoadKeepAliveAccess, LazyThreadSafetyMode.ExecutionAndPublication);

	private static readonly Lock PatchLock = new();

	private static bool _patchTried;

	private static string? _patchProblem;

	/// <summary>
	/// Why a channel open of an unknown type would still end a connection made now: null once
	/// <see cref="AcceptUnknownChannelTypes"/> has changed the table, the reason it could not otherwise.
	/// </summary>
	public static string? UnknownChannelTypesProblem
	{
		get
		{
			lock (PatchLock)
			{
				return _patchTried ? _patchProblem : "SSH.NET's message table was not changed before this connection was made.";
			}
		}
	}

	/// <summary>Why a session cannot be reached or written to, or null when it can.</summary>
	public static string? SessionProblem => Access.Value.Problem;

	/// <summary>The session under a connected client, or null when SSH.NET no longer lets it be reached.</summary>
	public static Session? GetSession(BaseClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		return Access.Value.ReadSession is { } read ? read(client) as Session : null;
	}

	/// <summary>Writes messages onto <paramref name="session"/>, or null when SSH.NET no longer lets that happen.</summary>
	public static ISshMessageSender? CreateSender(Session session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return Access.Value.Send is { } send ? new SessionMessageSender(session, send) : null;
	}

	/// <summary>Why a keepalive that wants a reply cannot be built, or null when it can.</summary>
	public static string? KeepAliveProblem => KeepAlive.Value.Problem;

	/// <summary>A new <c>keepalive@openssh.com</c> global request that wants a reply, or null when none can be built.</summary>
	public static GlobalRequestMessage? CreateKeepAliveRequest() => KeepAlive.Value.Create?.Invoke();

	/// <summary>
	/// Makes SSH.NET read a channel open of a type it has no class for, such as <c>auth-agent@openssh.com</c>, instead of
	/// throwing inside its message loop, which ends the connection. The change is to SSH.NET's static message table, so it
	/// holds for every connection made afterwards; a connection made before it picks it up at its next key exchange,
	/// which empties and refills the table's slots. Runs once; returns null when the change is in place, or why it is not.
	/// </summary>
	public static string? AcceptUnknownChannelTypes()
	{
		lock (PatchLock)
		{
			if (!_patchTried)
			{
				_patchTried = true;
				try
				{
					_patchProblem = PatchChannelOpen();
				}
				catch (Exception ex) when (ex is ArgumentException or MemberAccessException or TargetInvocationException or InvalidOperationException or NotSupportedException or InvalidCastException)
				{
					_patchProblem = $"SSH.NET's message table could not be changed ({ex.GetType().Name}).";
				}
			}

			return _patchProblem;
		}
	}

	private static SessionAccess LoadSessionAccess()
	{
		try
		{
			MethodInfo? getter = typeof(BaseClient).GetProperty("Session", Instance)?.GetGetMethod(nonPublic: true);
			if (getter is null || getter.GetParameters().Length != 0)
			{
				return SessionAccess.Missing("BaseClient.Session");
			}

			MethodInfo? send = typeof(Session).GetMethod("SendMessage", Instance, [typeof(Message)]);
			if (send is null || send.ReturnType != typeof(void) || send.IsStatic)
			{
				return SessionAccess.Missing("Session.SendMessage(Message)");
			}

			return new SessionAccess(client => getter.Invoke(client, null), send.CreateDelegate<Action<Session, Message>>(), null);
		}
		catch (Exception ex) when (ex is ArgumentException or MemberAccessException or AmbiguousMatchException)
		{
			return new SessionAccess(null, null, $"SSH.NET's session could not be reached ({ex.GetType().Name}).");
		}
	}

	/// <summary>
	/// Finds the constructor that takes a request name and a reply flag, and checks once that a message built through it
	/// says what it was given, since <see cref="GlobalRequestMessage"/> writes exactly those two fields.
	/// </summary>
	private static KeepAliveAccess LoadKeepAliveAccess()
	{
		try
		{
			ConstructorInfo? constructor = typeof(GlobalRequestMessage).GetConstructor(Instance, [typeof(byte[]), typeof(bool)]);
			if (constructor is null)
			{
				return KeepAliveAccess.Missing("GlobalRequestMessage(byte[], bool)");
			}

			GlobalRequestMessage Create() => (GlobalRequestMessage)constructor.Invoke([Encoding.ASCII.GetBytes(KeepAliveRequestName), true]);

			GlobalRequestMessage sample = Create();
			return string.Equals(sample.RequestName, KeepAliveRequestName, StringComparison.Ordinal) && sample.WantReply
				? new KeepAliveAccess(Create, null)
				: new KeepAliveAccess(null, "SSH.NET's global request no longer keeps the name and reply flag it is given.");
		}
		catch (Exception ex) when (ex is ArgumentException or MemberAccessException or TargetInvocationException or AmbiguousMatchException or InvalidCastException)
		{
			return new KeepAliveAccess(null, $"SSH.NET's global request could not be built ({ex.GetType().Name}).");
		}
	}

	/// <summary>
	/// Replaces the table entry that builds <see cref="ChannelOpenMessage"/> with one that builds
	/// <see cref="TolerantChannelOpenMessage"/>, in both places SSH.NET keeps it: the array a key exchange refills from and
	/// the name lookup a connect registers through. A table that does not look exactly as expected is left alone.
	/// </summary>
	private static string? PatchChannelOpen()
	{
		Type? factory = typeof(Session).Assembly.GetType("Renci.SshNet.SshMessageFactory");
		Type? metadata = factory?.GetNestedType("MessageMetadata", BindingFlags.NonPublic | BindingFlags.Public);
		Type? typedMetadata = factory?.GetNestedType("MessageMetadata`1", BindingFlags.NonPublic | BindingFlags.Public);
		FieldInfo? allField = factory?.GetField("AllMessages", Static);
		FieldInfo? byNameField = factory?.GetField("MessagesByName", Static);
		PropertyInfo? idProperty = metadata?.GetProperty("Id", Instance);
		PropertyInfo? nameProperty = metadata?.GetProperty("Name", Instance);
		PropertyInfo? numberProperty = metadata?.GetProperty("Number", Instance);
		if (factory is null || metadata is null || typedMetadata is null || allField is null || byNameField is null
			|| idProperty is null || nameProperty is null || numberProperty is null)
		{
			return "SSH.NET's message table is not where this version expects it.";
		}

		if (allField.GetValue(null) is not Array all || byNameField.GetValue(null) is not IDictionary byName)
		{
			return "SSH.NET's message table has an unexpected shape.";
		}

		Type stock = typedMetadata.MakeGenericType(typeof(ChannelOpenMessage));
		Type tolerant = typedMetadata.MakeGenericType(typeof(TolerantChannelOpenMessage));
		for (int index = 0; index < all.Length; index++)
		{
			object? entry = all.GetValue(index);
			if (entry is null || !string.Equals(nameProperty.GetValue(entry) as string, ChannelOpenMessageName, StringComparison.Ordinal))
			{
				continue;
			}

			if (entry.GetType() == tolerant)
			{
				return null;
			}

			if (entry.GetType() != stock || !ReferenceEquals(byName[ChannelOpenMessageName], entry) || numberProperty.GetValue(entry) is not byte number || number != 90)
			{
				return "SSH.NET's channel open entry is not the one this version expects.";
			}

			object replacement = Activator.CreateInstance(
				tolerant,
				Instance,
				binder: null,
				[idProperty.GetValue(entry), ChannelOpenMessageName, number],
				culture: null)
				?? throw new InvalidOperationException("The replacement entry was not created.");

			// The name lookup first: a connect that registers in between still meets a consistent pair at its next key
			// exchange, because the exchange empties the slot before refilling it from the array.
			byName[ChannelOpenMessageName] = replacement;
			all.SetValue(replacement, index);
			string? problem;
			try
			{
				problem = VerifyPatch(factory);
			}
			catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or MemberAccessException or AmbiguousMatchException)
			{
				problem = $"SSH.NET's changed message table failed its check ({ex.GetType().Name}).";
			}

			if (problem is not null)
			{
				byName[ChannelOpenMessageName] = entry;
				all.SetValue(entry, index);
			}

			return problem;
		}

		return "SSH.NET's message table has no channel open entry.";
	}

	/// <summary>Builds a message table the way a connect does and checks that a channel open now comes out tolerant.</summary>
	private static string? VerifyPatch(Type factory)
	{
		object? instance = Activator.CreateInstance(factory, nonPublic: true);
		MethodInfo? enable = factory.GetMethod("EnableAndActivateMessage", Instance, [typeof(string)]);
		MethodInfo? create = factory.GetMethod("Create", Instance, [typeof(byte)]);
		if (instance is null || enable is null || create is null)
		{
			return "SSH.NET's message table cannot be checked.";
		}

		_ = enable.Invoke(instance, [ChannelOpenMessageName]);
		return create.Invoke(instance, [(byte)90]) is TolerantChannelOpenMessage
			? null
			: "SSH.NET did not take the changed channel open entry.";
	}

	private sealed record SessionAccess(Func<BaseClient, object?>? ReadSession, Action<Session, Message>? Send, string? Problem)
	{
		public static SessionAccess Missing(string member) => new(null, null, $"SSH.NET no longer has {member}.");
	}

	private sealed record KeepAliveAccess(Func<GlobalRequestMessage>? Create, string? Problem)
	{
		public static KeepAliveAccess Missing(string member) => new(null, $"SSH.NET no longer has {member}.");
	}
}
