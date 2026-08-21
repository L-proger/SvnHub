# SvnHub

SvnHub is a lightweight web panel for managing Subversion repositories, users,
groups, and Apache SVN auth files.

## Development Run

```sh
dotnet run --project src/SvnHub.Web
```

- Health check: `GET /health`
- First setup page: `GET /Setup`
- Main configuration file: `src/SvnHub.Web/appsettings.json`

## Docker Deployment

Docker deployment files live in `deploy/docker/`. The container runs SvnHub UI
and Apache SVN DAV together; persistent data and repositories are mounted from
the host.

From the repository root:

```sh
cp deploy/docker/.env.example deploy/docker/.env
```

Edit `deploy/docker/.env`:

```env
SVNHUB_DATA=/mnt/raid/svnhub_data
SVNHUB_REPOS=/mnt/raid/svn_repositories
SVNHUB_UID=1001
SVNHUB_GID=1001
SVNHUB_FIX_OWNERSHIP=0
SVNHUB_MAX_PREVIEW_BYTES=52428800
```

For existing repositories, set `SVNHUB_UID` and `SVNHUB_GID` to the numeric
owner/group of the repository tree:

```sh
stat -c '%u:%g %n' /mnt/raid/svn_repositories
```

Start or rebuild the container:

```sh
cd deploy/docker
docker compose up -d --build
```

If `docker compose version` fails, install the Compose plugin
(`sudo apt install docker-compose-v2`) or run `docker-compose up -d --build`
from `deploy/docker`.

Read the full Docker guide in `deploy/docker/README.md`.

`SVNHUB_MAX_PREVIEW_BYTES` limits how much SvnHub will read into the web UI for
file preview, raw, and download responses. Larger files remain available through
normal SVN checkout/update.

## Application Tokens

SvnHub exposes a read-only integration endpoint at `/mcp`. Users can create
personal application tokens from the account menu at `/account`. External
clients should send the token as:

```http
Authorization: Bearer svnhub_app_...
```

Personal token metadata is stored in `api-tokens.json`; only token hashes are
stored, and the plain token is shown once after creation. Repository permissions
are evaluated for the token owner.

Browser-based MCP clients running on loopback origins such as
`http://127.0.0.1:1235` or `http://localhost:1235` are allowed automatically.
For a UI hosted on another machine or hostname, add its exact origin (scheme,
host, and port, without a path) to `SvnHub:McpAllowedOrigins`. Docker deployments
can set one origin through `SVNHUB_MCP_ALLOWED_ORIGIN`; additional origins can be
configured as `SvnHub__McpAllowedOrigins__1`, `__2`, and so on.

## Runtime Data

Keep SvnHub runtime data outside the Git checkout. The data directory contains:

- `config.json`
- `users.json`
- `repos.json`
- `groups.json`
- `permissions.json`
- `api-tokens.json`
- `audit.json`
- `authz`
- `htpasswd`

Do not commit `deploy/docker/.env`, generated auth files, or repository contents.

## Deployment Templates

Additional templates are in `deploy/`:

- `deploy/apache/` - host Apache reverse proxy examples.
- `deploy/systemd/` - service unit examples.
- `deploy/sudoers/` - restricted sudo example.
