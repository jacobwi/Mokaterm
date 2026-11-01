FROM ubuntu:24.04

# The live tests' Linux: one machine with the commands the System view reads, sudo for the root paths, and a
# Docker daemon of its own so the Docker manager meets containers for real. The user's password matches the
# compose file's MOKATERM_TEST_SSH example.
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
		openssh-server \
		sudo \
		curl \
		ca-certificates \
		procps \
		iproute2 \
		iputils-ping \
		net-tools \
		cron \
		rsyslog \
		systemd \
		systemd-sysv \
		ufw \
		htop \
		less \
		jq \
		vim-tiny \
		tzdata \
		xauth \
	&& rm -rf /var/lib/apt/lists/*

# The Docker manager: the CLI, and the daemon (dind) started by the entry point.
RUN curl -fsSL https://get.docker.com | sh

# A user the tests connect as, with sudo without a password (the root paths ask through the session instead).
# The docker group too, so the Docker manager's live tests reach the inner daemon's socket.
RUN useradd -m -s /bin/bash test -G docker \
	&& echo 'test:testing123' | chpasswd \
	&& echo 'test ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/test \
	&& chmod 0440 /etc/sudoers.d/test

# Password sign-in is what the tests use; root over SSH is off. The daemon also answers on 2222, the port the
# host publishes, so a jump through this server reaches it on the port callers know (the jump test's assumption).
# X11 forwarding needs the xauth above as well: without it sshd accepts the x11 request but never sets $DISPLAY.
RUN mkdir -p /run/sshd /var/run/sshd \
	&& sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication yes/' /etc/ssh/sshd_config \
	&& sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config \
	&& echo 'Port 22' >> /etc/ssh/sshd_config \
	&& echo 'Port 2222' >> /etc/ssh/sshd_config \
	&& echo 'X11Forwarding yes' >> /etc/ssh/sshd_config

# A few things the reads list, so the pages have something to say.
RUN mkdir -p /var/log/journal && (cron || true)

COPY start.sh /usr/local/bin/start.sh
RUN chmod +x /usr/local/bin/start.sh

EXPOSE 22

ENTRYPOINT ["/usr/local/bin/start.sh"]
