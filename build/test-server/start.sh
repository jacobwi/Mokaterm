#!/bin/bash
# Everything the live tests talk to starts here: the SSH daemon they connect through, the journal the log page
# reads, cron for the cron page, and the inner Docker daemon for the Docker manager.
set -e

# The container is privileged; systemd gives the services page real units to list. When systemd will not start
# (a host without the cgroup v2 layout it wants), the rest still comes up and the pages read what exists.
if [ -d /sys/fs/cgroup ]; then
	/lib/systemd/systemd >/var/log/systemd.out 2>&1 &
	sleep 2
fi

cron || true
rsyslogd || true

# The inner Docker daemon: the manager only reads, so a vfs storage driver keeps it simple and small.
if command -v dockerd >/dev/null 2>&1; then
	dockerd --storage-driver=vfs --iptables=false >/var/log/dockerd.out 2>&1 &
	sleep 3

	# Something to look at: one container from a tiny image, running but idle.
	docker run -d --name mokaterm-test busybox:latest sh -c 'while true; do sleep 3600; done' \
		>/var/log/test-container.out 2>&1 || true
fi

exec /usr/sbin/sshd -D -e
