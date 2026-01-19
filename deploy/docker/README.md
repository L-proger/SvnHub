# SvnHub in Docker (HTTP inside, HTTPS on host)

This setup runs **SvnHub + Apache (SVN DAV)** inside a single container on **HTTP**.
Your **host Apache2** terminates **HTTPS** and reverse-proxies to the container.

## Configure host paths (bind mounts)

This setup uses **bind mounts** so you control where data lives on the host.

1) Copy `.env.example` to `.env` and edit paths:
- `SVNHUB_DATA=/srv/svnhub/data`
- `SVNHUB_REPOS=/srv/svnhub/repos`

2) Create the directories on the host:
- `sudo mkdir -p /srv/svnhub/data /srv/svnhub/repos`

These map to container paths:
- `/var/lib/svnhub/repos` — SVN repositories (`RepositoriesRootPath`)
- `/var/lib/svnhub/data` — SvnHub state + generated auth files
  - `config.json`
  - `users.json`
  - `repos.json`
  - `groups.json`
  - `permissions.json`
  - `audit.json`
  - `authz`
  - `htpasswd`

## Run (docker compose)

From repo root:
- `cp deploy/docker/.env.example deploy/docker/.env`
- `docker compose -f deploy/docker/docker-compose.yml up -d --build`

The container binds to `127.0.0.1:8080` by default.

## Host Apache2 (HTTPS)

Use `deploy/docker/host-apache-ssl-proxy.conf` as an example vhost:
- terminates TLS on `:443`
- proxies all traffic to `http://127.0.0.1:8080/`

## Notes

- The container uses internal Apache for `/svn` and proxies `/` to Kestrel (SvnHub UI).
- By default the container entrypoint attempts `chown -R www-data:www-data` on both mounted volumes.
  - For large bind-mounts you may want to do this once on the host and set `SVNHUB_SKIP_CHOWN=1`.

