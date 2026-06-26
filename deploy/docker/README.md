# SvnHub in Docker (HTTP inside, HTTPS on host)

This setup runs **SvnHub + Apache (SVN DAV)** inside a single container on **HTTP**.
Your **host Apache2** terminates **HTTPS** and reverse-proxies to the container.

## Configure host paths (bind mounts)

This setup uses **bind mounts** so you control where data lives on the host.

1) Copy the committed example file to the local runtime `.env` file:

```sh
cp deploy/docker/.env.example deploy/docker/.env
```

The real `deploy/docker/.env` is ignored by Git and should stay local to the
server.

2) Edit `deploy/docker/.env`:

- `SVNHUB_DATA=/srv/svnhub/data`
- `SVNHUB_REPOS=/srv/svnhub/repos`
- `SVNHUB_UID=10001`
- `SVNHUB_GID=10001`
- `SVNHUB_MAX_PREVIEW_BYTES=52428800`

3) Create the directories on the host:
- `sudo mkdir -p /srv/svnhub/data /srv/svnhub/repos`

These map to container paths:
- `/var/lib/svnhub/repos` — SVN repositories (`RepositoriesRootPath`)
- `/var/lib/svnhub/data` — SvnHub state + generated auth files
  - `config.json`
  - `users.json`
  - `repos.json`
  - `groups.json`
  - `permissions.json`
  - `api-tokens.json`
  - `audit.json`
  - `data-protection-keys/`
  - `authz`
  - `htpasswd`

The `data-protection-keys/` directory stores ASP.NET keys used to validate
browser login cookies. Keep it in the persistent data mount; otherwise users
will be asked to log in again after the container is recreated.

## File ownership model

Inside the container SvnHub runs as a single service user named `svnhub`.
Both the ASP.NET app and Apache SVN workers use this identity, so files created
through the UI and files written by Subversion have the same owner.

Linux bind mounts use numeric IDs, not names. By default the service identity is:
- `SVNHUB_UID=10001`
- `SVNHUB_GID=10001`

For a new empty install, the entrypoint initializes the top-level mounted
directories for that UID/GID. It does not recursively change non-empty
repositories by default.

## Existing Repositories

For an existing Subversion repository tree, prefer matching the container service
identity to the host owner instead of recursively changing ownership.

1) Find the current numeric owner/group:

```sh
stat -c '%u:%g %n' /srv/svn/repos
find /srv/svn/repos -mindepth 1 -maxdepth 1 -type d -exec stat -c '%u:%g %n' {} \;
```

2) Put those numbers in `.env`:

```env
SVNHUB_REPOS=/srv/svn/repos
SVNHUB_UID=1001
SVNHUB_GID=1001
SVNHUB_FIX_OWNERSHIP=0
```

3) Make sure the data directory is writable by the same service identity. For a
new SvnHub data directory, either leave it empty and let the entrypoint initialize
the top-level directory, or prepare it explicitly:

```sh
sudo mkdir -p /srv/svnhub/data
sudo chown 1001:1001 /srv/svnhub/data
sudo chmod 2770 /srv/svnhub/data
```

4) Start the container and use the UI's Discover action to register repositories.
Discover reads repository folders and writes SvnHub metadata; it does not rewrite
repository contents.

Use `SVNHUB_FIX_OWNERSHIP=1` only when you intentionally want SvnHub to take over
both mounted trees:

```env
SVNHUB_FIX_OWNERSHIP=1
```

Leave `SVNHUB_FIX_OWNERSHIP=0` for normal operation after ownership is correct.

## Run (docker compose)

From repo root:

```sh
cd deploy/docker
docker compose up -d --build
```

If `docker compose version` fails, install the Compose plugin or use the legacy
standalone command:

```sh
sudo apt update
sudo apt install docker-compose-v2
docker compose version
```

Fallback:

```sh
docker-compose up -d --build
```

The container binds to `127.0.0.1:8080` by default.

## Browser preview limit

`SVNHUB_MAX_PREVIEW_BYTES` controls the largest file SvnHub will read into the
web UI for preview, raw, and download responses. The default is 50 MB
(`52428800`). Larger files are not loaded by the UI process; use SVN checkout or
the repository SVN URL for them.

## Application tokens

SvnHub exposes a read-only integration endpoint at `/mcp`. Users can create
personal application tokens from the account menu at `/account`. External
clients should send:

```http
Authorization: Bearer svnhub_app_...
```

Personal token metadata is stored in `/var/lib/svnhub/data/api-tokens.json`.
Only token hashes are stored, and normal repository permissions still apply for
the token owner.

## Update

When this repository has been updated in place, rebuild and recreate the
container from the new source tree. The SVN repositories and SvnHub state live in
the host bind mounts from `.env`, so recreating the container does not move or
delete them.

From repo root:

```sh
cd deploy/docker
docker compose up -d --build --remove-orphans
```

Then check startup logs:

```sh
docker compose logs -f --tail=100
```

On Windows/PowerShell you can run the helper:

```powershell
powershell -ExecutionPolicy Bypass -File deploy/docker/update.ps1
```

Do not use `down -v` for normal updates. If you need to roll back, check out the
previous repository version and run the same update command again.

## Host Apache2 (HTTPS)

Use `deploy/docker/host-apache-ssl-proxy.conf` as an example vhost:
- terminates TLS on `:443`
- proxies all traffic to `http://127.0.0.1:8080/`

## Notes

- The container uses internal Apache for `/svn` and proxies `/` to Kestrel (SvnHub UI).
- If mounted directories are not writable by the configured UID/GID, startup fails with a permissions hint instead of silently changing repository ownership.
