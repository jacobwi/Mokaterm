using Mokaterm.Modules.Local.Containers;

namespace Mokaterm.Modules.Local.Tests;

public sealed class ContainerNamesTests
{
	[Theory]
	[InlineData("default")]
	[InlineData("desktop-linux")]
	[InlineData("remote.box+1")]
	public void DockerContext_DockersOwnRule_Passes(string context) => Assert.Null(ContainerNames.DockerContextProblem(context));

	[Theory]
	[InlineData("a")]
	[InlineData("-x")]
	[InlineData("my context")]
	[InlineData("team/box")]
	public void DockerContext_AnythingElse_IsRefused(string context) => Assert.NotNull(ContainerNames.DockerContextProblem(context));

	[Theory]
	[InlineData("ssh://ada@build")]
	[InlineData("tcp://10.0.0.5:2376")]
	[InlineData("npipe:////./pipe/docker_engine")]
	[InlineData("unix:///var/run/docker.sock")]
	[InlineData("SSH://ada@build:2222")]
	public void DockerHost_TheSchemesTheCliDials_Pass(string host) => Assert.Null(ContainerNames.DockerHostProblem(host));

	[Theory]
	[InlineData("build")]
	[InlineData("http://build")]
	[InlineData("ssh://")]
	[InlineData("ssh://ada@build extra")]
	public void DockerHost_AnythingElse_IsRefused(string host) => Assert.NotNull(ContainerNames.DockerHostProblem(host));

	[Theory]
	[InlineData("root", true)]
	[InlineData("1000", true)]
	[InlineData("1000:1000", true)]
	[InlineData("www-data:www-data", true)]
	[InlineData("svc$app", true)]
	[InlineData("-u", false)]
	[InlineData("root user", false)]
	[InlineData("a:b:c", false)]
	public void User_NameOrUid_WithAGroup(string user, bool passes) => Assert.Equal(passes, ContainerNames.UserProblem(user) is null);

	[Theory]
	[InlineData("/", true)]
	[InlineData("/srv/my app", true)]
	[InlineData(@"C:\inetpub", true)]
	[InlineData("c:/app", true)]
	[InlineData("srv", false)]
	[InlineData("~/app", false)]
	[InlineData("/srv\tapp", false)]
	public void Folder_AbsoluteInsideTheContainer(string folder, bool passes) => Assert.Equal(passes, ContainerNames.FolderProblem(folder) is null);

	[Theory]
	[InlineData("web-7d9f8b6c5-x2k4q", true)]
	[InlineData("a", true)]
	[InlineData("etcd.kube-system.node1", true)]
	[InlineData("Web", false)]
	[InlineData("web-", false)]
	[InlineData("-web", false)]
	[InlineData("web_1", false)]
	[InlineData("web..1", false)]
	public void Pod_IsADnsSubdomain(string pod, bool passes) => Assert.Equal(passes, ContainerNames.PodProblem(pod) is null);

	[Fact]
	public void Pod_LongerThanKubernetesAllows_IsRefused()
	{
		Assert.Null(ContainerNames.PodProblem(new string('a', 253)));
		Assert.NotNull(ContainerNames.PodProblem(new string('a', 254)));
	}

	[Theory]
	[InlineData("kube-system", true)]
	[InlineData("shop", true)]
	[InlineData("shop.prod", false)]
	[InlineData("Shop", false)]
	public void NamespaceAndContainer_AreDnsLabels(string name, bool passes)
	{
		Assert.Equal(passes, ContainerNames.NamespaceProblem(name) is null);
		Assert.Equal(passes, ContainerNames.PodContainerProblem(name) is null);
	}

	[Fact]
	public void Label_LongerThan63_IsRefused()
	{
		Assert.Null(ContainerNames.NamespaceProblem(new string('a', 63)));
		Assert.NotNull(ContainerNames.NamespaceProblem(new string('a', 64)));
	}

	[Theory]
	[InlineData("arn:aws:eks:eu-west-1:123456789012:cluster/prod", true)]
	[InlineData("gke_project_europe-west1_prod", true)]
	[InlineData("ada@kind kind", true)]
	[InlineData("line\nbreak", false)]
	public void KubeContext_RefusesOnlyWhatACommandLineShouldNotCarry(string context, bool passes) =>
		Assert.Equal(passes, ContainerNames.KubeContextProblem(context) is null);

	[Fact]
	public void Value_TrimsAndTurnsBlankIntoNothing()
	{
		Assert.Equal("web", ContainerNames.Value("  web "));
		Assert.Null(ContainerNames.Value("   "));
		Assert.Null(ContainerNames.Value(null));
	}
}
