FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish ./src/SvnHub.Web/SvnHub.Web.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
SHELL ["/bin/bash", "-c"]

RUN apt-get update \
    && apt-get install -y --no-install-recommends apache2 apache2-utils subversion libapache2-mod-svn ca-certificates gosu \
    && rm -rf /var/lib/apt/lists/*

# SvnHub uses one service identity for the web app, Apache SVN workers, and SVN CLI tools.
RUN groupadd --gid 10001 --system svnhub \
    && useradd --uid 10001 --gid svnhub --system --home-dir /var/lib/svnhub --shell /usr/sbin/nologin svnhub \
    && mkdir -p /var/lib/svnhub/data /var/lib/svnhub/repos \
    && chown -R svnhub:svnhub /var/lib/svnhub

# Enable required apache modules.
RUN a2enmod dav dav_svn authz_svn proxy proxy_http headers \
    && a2dissite 000-default

# Apache starts as root, then serves requests as the same service user used by SvnHub.
RUN sed -i \
    -e 's/^export APACHE_RUN_USER=.*/export APACHE_RUN_USER=svnhub/' \
    -e 's/^export APACHE_RUN_GROUP=.*/export APACHE_RUN_GROUP=svnhub/' \
    /etc/apache2/envvars

# Listen on 8080 inside container (disable 443 listener here; TLS terminates on the host).
RUN sed -i -e 's/Listen 80/Listen 8080/' -e 's/^Listen 443$/#Listen 443/' /etc/apache2/ports.conf

WORKDIR /app
COPY --from=build /out/ /app/

COPY deploy/docker/apache/svnhub-container.conf /etc/apache2/sites-available/svnhub.conf
RUN a2ensite svnhub

COPY deploy/docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Default paths inside the container (override via env vars if needed).
ENV SvnHub__DataDirectory=/var/lib/svnhub/data \
    SvnHub__RepositoriesRootPath=/var/lib/svnhub/repos \
    SvnHub__ApacheReloadProgram= \
    SvnHub__ApacheReloadArguments= \
    SvnHub__MaxPreviewBytes=52428800 \
    ASPNETCORE_URLS=http://127.0.0.1:5000 \
    SVNHUB_UID=10001 \
    SVNHUB_GID=10001 \
    SVNHUB_FIX_OWNERSHIP=0

EXPOSE 8080
ENTRYPOINT ["/entrypoint.sh"]
