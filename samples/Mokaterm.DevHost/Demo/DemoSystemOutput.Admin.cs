using System.Globalization;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// The administration reads of the system view (authorized keys, the security page, reboot and security update checks,
/// unit files) as the same Ubuntu server would print them. The keys are well-formed ed25519 keys that belong to nobody.
/// </summary>
internal static partial class DemoSystemOutput
{
	private const string LaptopKey = "AAAAC3NzaC1lZDI1NTE5AAAAIF7sDcQZqoM3v3JfAm/anHjByxxkLur/nW4REvN3g+lC";

	private const string CiKey = "AAAAC3NzaC1lZDI1NTE5AAAAIAX+ddjQ0/zM7n5OhO9mn3zqmoqGxcAfb4Kq5zkp9003";

	private const string BackupKey = "AAAAC3NzaC1lZDI1NTE5AAAAIFTQDYZ3WM74FrxGhfWOMnuUlxKwfr0Xw0hfP/yen1Ez";

	/// <summary>The output of an administration read, or null for a script this part does not answer.</summary>
	private static string? ForAdmin(string script, DemoAccount account, string hostName, DateTimeOffset now) => script switch
	{
		"keys.read" => $"""
			@@mt:home
			{account.Home}
			@@mt:state
			present
			@@mt:folder-mode
			700 {account.Name}
			@@mt:file-mode
			600 {account.Name}
			@@mt:size
			412
			@@mt:keys
			# laptop and the CI runner
			ssh-ed25519 {LaptopKey} {account.Name}@laptop
			from="10.0.0.0/8",no-pty,no-agent-forwarding ssh-ed25519 {CiKey} deploy-ci
			command="/usr/local/bin/backup-receive",restrict ssh-ed25519 {BackupKey} backup@nas
			""",
		"security.read" => Security(account, hostName, now),
		// apt's security updates are the same list, read by suite.
		"packages.security" => For("packages.updates", account, hostName, now),
		"reboot.status" => """
			@@mt:flag
			*** System restart required ***
			@@mt:packages
			libc6
			linux-image-6.8.0-47-generic
			@@mt:flag-watch
			""",
		"services.cat" => """
			# /usr/lib/systemd/system/nginx.service
			[Unit]
			Description=A high performance web server and a reverse proxy server
			Documentation=man:nginx(8)
			After=network-online.target remote-fs.target nss-lookup.target
			Wants=network-online.target

			[Service]
			Type=forking
			PIDFile=/run/nginx.pid
			ExecStartPre=/usr/sbin/nginx -t -q -g 'daemon on; master_process on;'
			ExecStart=/usr/sbin/nginx -g 'daemon on; master_process on;'
			ExecReload=/usr/sbin/nginx -g 'daemon on; master_process on;' -s reload
			TimeoutStopSec=5
			KillMode=mixed

			[Install]
			WantedBy=multi-user.target

			# /etc/systemd/system/nginx.service.d/override.conf
			[Service]
			LimitNOFILE=65536
			""",

		// The sample plugin's fail2ban page (samples/Mokaterm.Plugins.Sample), the same jails the security page reads.
		"fail2ban.status" => """
			Status
			|- Number of jail:	2
			`- Jail list:	nginx-http-auth, sshd
			@@f2b:jail nginx-http-auth
			Status for the jail: nginx-http-auth
			|- Filter
			|  |- Currently failed:	0
			|  |- Total failed:	41
			|  `- File list:	/var/log/nginx/error.log
			`- Actions
			   |- Currently banned:	2
			   |- Total banned:	7
			   `- Banned IP list:	203.0.113.77 2001:db8:4::1f
			@@f2b:jail sshd
			Status for the jail: sshd
			|- Filter
			|  |- Currently failed:	2
			|  |- Total failed:	1288
			|  `- Journal matches:	_SYSTEMD_UNIT=ssh.service + _COMM=sshd
			`- Actions
			   |- Currently banned:	1
			   |- Total banned:	93
			   `- Banned IP list:	198.51.100.23
			""",
		_ => null,
	};

	private static string Security(DemoAccount account, string hostName, DateTimeOffset now)
	{
		DateTimeOffset at = now.AddHours(-3);
		string Stamp(int minutes) => at.AddMinutes(minutes).ToString("yyyy-MM-dd'T'HH:mm:ss'+0000'", CultureInfo.InvariantCulture);
		return $"""
			@@mt:uid
			{(account.IsRoot ? "0" : "1000")}
			@@mt:sshd
			/usr/sbin/sshd
			@@mt:sshd-t
			port 22
			addressfamily any
			listenaddress [::]:22
			listenaddress 0.0.0.0:22
			usepam yes
			permitrootlogin without-password
			maxauthtries 6
			pubkeyauthentication yes
			passwordauthentication yes
			kbdinteractiveauthentication no
			x11forwarding yes
			permitemptypasswords no
			subsystem sftp /usr/lib/openssh/sftp-server
			@@mt:sshd-t-exit
			0
			@@mt:sshd-file /etc/ssh/sshd_config
			Include /etc/ssh/sshd_config.d/*.conf

			Port 22
			PermitRootLogin prohibit-password
			PasswordAuthentication no
			KbdInteractiveAuthentication no
			UsePAM yes
			X11Forwarding yes
			PrintMotd no
			AcceptEnv LANG LC_*
			Subsystem sftp /usr/lib/openssh/sftp-server

			@@mt:sshd-file /etc/ssh/sshd_config.d/50-cloud-init.conf
			PasswordAuthentication yes

			@@mt:logins-source
			journal
			@@mt:logins
			{Stamp(0)} {hostName} sshd[48210]: Invalid user admin from 198.51.100.23 port 40122
			{Stamp(0)} {hostName} sshd[48210]: Failed password for invalid user admin from 198.51.100.23 port 40122 ssh2
			{Stamp(1)} {hostName} sshd[48214]: Failed password for root from 198.51.100.23 port 40180 ssh2
			{Stamp(2)} {hostName} sshd[48220]: Failed password for root from 198.51.100.23 port 40210 ssh2
			{Stamp(31)} {hostName} sshd[48301]: Invalid user oracle from 203.0.113.77 port 51544
			{Stamp(64)} {hostName} sshd[48412]: Failed publickey for deploy from 192.0.2.40 port 60122 ssh2: ED25519 SHA256:6TDyFp4Q958PJo1QO1OQjggsQW2oXmmL2cOlxObcweg
			{Stamp(95)} {hostName} sshd[48530]: Failed password for {account.Name} from 192.0.2.15 port 55012 ssh2
			@@mt:fail2ban
			Status
			|- Number of jail:	1
			`- Jail list:	sshd
			@@mt:jail sshd
			Status for the jail: sshd
			|- Filter
			|  |- Currently failed:	2
			|  |- Total failed:	1288
			|  `- Journal matches:	_SYSTEMD_UNIT=ssh.service + _COMM=sshd
			`- Actions
			   |- Currently banned:	1
			   |- Total banned:	93
			   `- Banned IP list:	198.51.100.23
			@@mt:ss
			Netid State  Recv-Q Send-Q Local Address:Port  Peer Address:PortProcess
			udp   UNCONN 0      0      127.0.0.53%lo:53         0.0.0.0:*
			tcp   LISTEN 0      4096         0.0.0.0:22         0.0.0.0:*    users:(("sshd",pid=801,fd=3))
			tcp   LISTEN 0      511          0.0.0.0:80         0.0.0.0:*    users:(("nginx",pid=1123,fd=6))
			tcp   LISTEN 0      200        127.0.0.1:5432       0.0.0.0:*    users:(("postgres",pid=1310,fd=5))
			tcp   LISTEN 0      511                *:6379             *:*    users:(("redis-server",pid=1402,fd=7))
			tcp   LISTEN 0      4096            [::]:22            [::]:*    users:(("sshd",pid=801,fd=4))
			""";
	}
}
