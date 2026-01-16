# SvnHub (MVP)

Упрощённая web‑панель управления Subversion

## Запуск в dev
- `dotnet run --project src/SvnHub.Web`
- Healthcheck: `GET /health`
- Первый запуск: `GET /Setup`

## Настройки

Файл: `src/SvnHub.Web/appsettings.json`

Секция `SvnHub`:
- `DataFilePath` — путь к JSON состоянию (может быть относительным).
- `RepositoriesRootPath` — корень репозиториев на диске.
- `HtpasswdPath` — куда писать `htpasswd`.
- `AuthzPath` — куда писать `authz`.
- `HtpasswdCommand` — команда `htpasswd`.
- `SvnadminCommand` — команда `svnadmin`.
- `ApacheReloadProgram` + `ApacheReloadArguments` — как перезагружать Apache.

## Деплой на Ubuntu 

Смотрите шаблоны в `deploy/`:
- `deploy/apache/SvnHub.conf` — vhost: `/svn` (DAV SVN) + reverse proxy на Kestrel.
- `deploy/systemd/SvnHub.service` — systemd unit для приложения.
- `deploy/sudoers/SvnHub` — пример ограниченного sudo (опционально).

Приложение должно иметь права на запись в `HtpasswdPath`, `AuthzPath`, и на создание каталогов в `RepositoriesRootPath`.

