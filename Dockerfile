FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish ./src/SvnHub.Web/SvnHub.Web.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
SHELL ["/bin/bash", "-c"]

RUN apt-get update \
    && apt-get install -y --no-install-recommends apache2 apache2-utils subversion libapache2-mod-svn ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Enable required apache modules.
RUN a2enmod dav dav_svn authz_svn proxy proxy_http headers \
    && a2dissite 000-default

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
    SvnHub__ApacheReloadProgram=apache2ctl \
    SvnHub__ApacheReloadArguments="-k graceful" \
    ASPNETCORE_URLS=http://127.0.0.1:5000

EXPOSE 8080
ENTRYPOINT ["/entrypoint.sh"]
