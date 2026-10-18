using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// Builds, tags and pushes as the exact lines that run and that a confirmation shows, build argument values masked; the
/// server's own time limit around the long ones, as the login and through sudo; and what the health watch makes of them.
/// </summary>
public sealed class DockerBuildTests
{
	private const string Mask = DockerInspectParser.Mask;

	private static readonly ComposeTarget Shop = new(new ComposeTool(DockerProgram.ComposePlugin, "2.29.7"), "shop", "/srv/shop", ["/srv/shop/compose.yaml"], []);

	private static readonly string Limit = "sh -c '" + DockerTimeLimit.Script + "' sh ";

	[Fact]
	public void BuildKit_IsBuildxWithPlainProgress_LoadedIntoTheHost_UnderTheServersLimit()
	{
		DockerBuildSpec spec = new()
		{
			Context = "/srv/app/",
			Tags = ["app:1.4", "", "registry.example.com/team/app:1.4"],
			Arguments = [new DockerRunVariable("NPM_TOKEN", "npm_s3cr3t"), new DockerRunVariable("", "")],
			Target = "runtime",
			Platform = "linux/arm64",
			NoCache = true,
			Pull = true,
		};

		DockerCall call = spec.ToCall(DockerBuilder.BuildKit);

		Assert.Equal(
			Limit + "7200 docker buildx build --progress=plain --load --pull --no-cache --platform linux/arm64 --target runtime --tag app:1.4 --tag registry.example.com/team/app:1.4 --build-arg 'NPM_TOKEN=" + Mask + "' /srv/app",
			call.Display);
		Assert.EndsWith("--build-arg NPM_TOKEN=npm_s3cr3t /srv/app", DockerCommandLine.Build(call), StringComparison.Ordinal);
		Assert.True(call.RunsLong);
		Assert.Equal(DockerCall.BuildTimeout + TimeSpan.FromMinutes(1), call.Timeout);
		Assert.Equal(DockerCall.LiveOutputBytes, call.OutputLimitBytes);
		Assert.DoesNotContain("npm_s3cr3t", spec.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("npm_s3cr3t", call.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Legacy_IsPlainDockerBuild()
	{
		DockerBuildSpec spec = new() { Context = "/srv/app", Tags = ["app"], Dockerfile = "docker/Dockerfile.prod" };

		Assert.Equal(Limit + "7200 docker build --file /srv/app/docker/Dockerfile.prod --tag app /srv/app", spec.ToCall(DockerBuilder.Legacy).Display);
	}

	[Fact]
	public void Dockerfile_InTheContext_IsLeftToDocker_AndAnAbsoluteOneIsKept()
	{
		Assert.Null(new DockerBuildSpec { Context = "/srv/app", Dockerfile = "Dockerfile" }.DockerfilePath);
		Assert.Null(new DockerBuildSpec { Context = "/srv/app" }.DockerfilePath);
		Assert.Equal("/opt/files/Dockerfile", new DockerBuildSpec { Context = "/srv/app", Dockerfile = "/opt/files/Dockerfile" }.DockerfilePath);
		Assert.Equal("/srv/app/../shared/Dockerfile", new DockerBuildSpec { Context = "/srv/app/", Dockerfile = "../shared/Dockerfile" }.DockerfilePath);
	}

	[Fact]
	public void Problems_AreKeptAgainstTheirFields()
	{
		DockerBuildSpec spec = new()
		{
			Context = "relative/folder",
			Tags = ["App:1", "app:1", "sha256:0123456789abcdef0123456789abcdef", "app:1"],
			Arguments = [new DockerRunVariable("1BAD", "x"), new DockerRunVariable("OK", "line\nbreak"), new DockerRunVariable("TWICE", "a"), new DockerRunVariable("TWICE", "b")],
			Target = "-stage",
			Platform = "linux/amd64,linux/arm64",
		};

		IReadOnlyDictionary<string, string> problems = spec.Problems();

		Assert.Contains(DockerBuildSpec.ContextField, problems.Keys);
		Assert.Contains(DockerBuildSpec.TagField(0), problems.Keys);
		Assert.DoesNotContain(DockerBuildSpec.TagField(1), problems.Keys);
		Assert.Contains(DockerBuildSpec.TagField(2), problems.Keys);
		Assert.Equal("app:1 is named twice.", problems[DockerBuildSpec.TagField(3)]);
		Assert.Contains(DockerBuildSpec.ArgumentField(0), problems.Keys);
		Assert.Contains(DockerBuildSpec.ArgumentField(1), problems.Keys);
		Assert.Contains("set twice", problems[DockerBuildSpec.ArgumentField(3)], StringComparison.Ordinal);
		Assert.Contains(DockerBuildSpec.TargetField, problems.Keys);
		Assert.Contains("one platform", problems[DockerBuildSpec.PlatformField], StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => spec.ToCall(DockerBuilder.BuildKit));
	}

	[Fact]
	public void Summary_SaysTheLimit_AndThatBuildArgumentsStayInTheHistory()
	{
		DockerBuildSpec spec = new() { Context = "/srv/app", Tags = ["app:1.4"], Arguments = [new DockerRunVariable("TOKEN", "x")] };

		string summary = spec.Summary(DockerBuilder.BuildKit);

		Assert.Contains("BuildKit", summary, StringComparison.Ordinal);
		Assert.Contains("2 h", summary, StringComparison.Ordinal);
		Assert.Contains("history", summary, StringComparison.Ordinal);
	}

	[Fact]
	public void Tag_ChecksTheTarget()
	{
		Assert.Equal("docker image tag app:1.4 registry.example.com/team/app:1.4", DockerCommands.Tag("app:1.4", " registry.example.com/team/app:1.4 ").Display);
		Assert.Equal("docker image tag sha256:" + new string('a', 64) + " app:dev", DockerCommands.Tag("sha256:" + new string('a', 64), "app:dev").Display);
		Assert.Throws<ArgumentException>(() => DockerCommands.Tag("app:1.4", "app@sha256:" + new string('a', 64)));
		Assert.Throws<ArgumentException>(() => DockerCommands.Tag("app:1.4", "abcdef123456"));
		Assert.Throws<ArgumentException>(() => DockerCommands.Tag("app:1.4", "-app"));
	}

	[Fact]
	public void Push_OneTag_OrEveryTagOfARepository()
	{
		Assert.Equal(Limit + "3600 docker push registry.example.com/team/app:1.4", DockerCommands.Push("registry.example.com/team/app:1.4", allTags: false).Display);
		Assert.Equal(Limit + "3600 docker push --all-tags registry.example.com:5000/team/app", DockerCommands.Push("registry.example.com:5000/team/app", allTags: true).Display);
		Assert.Throws<ArgumentException>(() => DockerCommands.Push("registry.example.com/team/app:1.4", allTags: true));
		Assert.Throws<ArgumentException>(() => DockerCommands.Push("app@sha256:" + new string('b', 64), allTags: false));
		Assert.Throws<ArgumentException>(() => DockerCommands.Push("--all-tags", allTags: false));
	}

	[Fact]
	public void TimeLimit_UnwrapsToTheCallInside()
	{
		DockerCall inner = new() { Program = DockerProgram.Docker, Arguments = ["build", "--build-arg", "A=b", "/srv/app"], MaskedArguments = new HashSet<int> { 2 } };

		DockerCall wrapped = DockerTimeLimit.Wrap(inner, TimeSpan.FromMinutes(30));
		DockerCall back = DockerTimeLimit.Unwrap(wrapped)!;

		Assert.Equal(inner.Arguments, back.Arguments);
		Assert.Equal(inner.MaskedArguments, back.MaskedArguments);
		Assert.Equal(TimeSpan.FromMinutes(30), DockerTimeLimit.LimitOf(wrapped));
		Assert.Null(DockerTimeLimit.Unwrap(inner));
		Assert.Null(DockerTimeLimit.LimitOf(inner));
		Assert.Contains("'A=" + Mask + "'", wrapped.Display, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => DockerTimeLimit.Wrap(DockerCall.Script("true", []), TimeSpan.FromMinutes(1)));
	}

	[Fact]
	public void TimeLimit_ThroughSudo_RunsInTheProjectFolder()
	{
		DockerCall build = ComposeCommands.Build(Shop, "api");

		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(build);

		Assert.Equal("cd -- /srv/shop && " + Limit + "7200 docker compose -p shop -f /srv/shop/compose.yaml build api", build.Display);
		Assert.Equal("cd -- \"$1\" || exit 1\nshift\n" + DockerTimeLimit.Script, script);
		Assert.Equal(["/srv/shop", "7200", "docker", "compose", "-p", "shop", "-f", "/srv/shop/compose.yaml", "build", "api"], arguments);
		Assert.DoesNotContain('\r', DockerTimeLimit.Script);
	}

	[Fact]
	public void HealthWatch_IsToldThatBuildsPushesAndLoginsTouchNoContainer()
	{
		DockerCall[] calls =
		[
			new DockerBuildSpec { Context = "/srv/app", Tags = ["app"] }.ToCall(DockerBuilder.BuildKit),
			new DockerBuildSpec { Context = "/srv/app" }.ToCall(DockerBuilder.Legacy),
			DockerCommands.Push("app:1", allTags: false),
			DockerCommands.Tag("app:1", "app:2"),
			ComposeCommands.Build(Shop),
			DockerRegistryCommands.Logout("ghcr.io"),
		];

		foreach (DockerCall call in calls)
		{
			DockerChangeScope scope = DockerChangeScope.Of(call);
			Assert.False(scope.All, call.Display);
			Assert.Empty(scope.Containers);
			Assert.Empty(scope.Projects);
		}

		Assert.Equal(["shop"], DockerChangeScope.Of(ComposeCommands.Up(Shop)).Projects);
	}

	[Fact]
	public void ComposeBuild_IsALivePlan_ForTheProjectOrOneService()
	{
		ComposeActionPlan project = ComposeActions.Plan("build", Shop, ["web", "api"])!;
		ComposeActionPlan service = ComposeActions.Plan("build", Shop, ["web", "api"], "api")!;

		Assert.True(project.Live);
		Assert.Equal("Build images for shop (web, api)", project.Title);
		Assert.EndsWith("docker compose -p shop -f /srv/shop/compose.yaml build", Assert.Single(project.Calls).Display, StringComparison.Ordinal);
		Assert.EndsWith(" build api", Assert.Single(service.Calls).Display, StringComparison.Ordinal);
		Assert.NotNull(project.Confirm);
		Assert.False(ComposeActions.Plan("pull", Shop, ["web"])!.Live);
	}

	[Theory]
	[InlineData("linux/amd64", true)]
	[InlineData("linux/arm64/v8", true)]
	[InlineData("Linux/AMD64", false)]
	[InlineData("linux", false)]
	public void Platform_IsOneOsAndArch(string platform, bool valid) => Assert.Equal(valid, DockerNames.PlatformProblem(platform) is null);

	[Theory]
	[InlineData("runtime", true)]
	[InlineData("build-stage.2", true)]
	[InlineData("2stage", false)]
	[InlineData("a b", false)]
	public void Stage_StartsWithALetter(string stage, bool valid) => Assert.Equal(valid, DockerNames.StageProblem(stage) is null);
}
