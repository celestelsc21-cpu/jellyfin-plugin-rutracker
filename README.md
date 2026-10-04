# Jellyfin.Plugin.RuTracker

Плагин Jellyfin 10.11.x: поиск на RuTracker, загрузка через qBittorrent (Windows),
просмотр во время загрузки, отслеживание обновлений.

**Статус: этап 3 из 7** — поиск, кнопка в панели Jellyfin и загрузка через qBittorrent.

## Загрузка

Кнопка **«Смотреть»** у раздачи (роль «Скачивание») открывает окно: тип, папка и серия,
с которой начать. Загружается вся раздача: сначала выбранная серия, затем следующие,
в конце — предыдущие. Текущая серия получает в qBittorrent максимальный приоритет,
следующая — высокий; когда серия готова, приоритет переходит дальше. На вкладке
**«Загрузки»** видны прогресс, текущая серия, смена серии и отмена.

Пока файлы кладутся прямо в выбранную папку медиатеки (режим «копировать после
загрузки» появится вместе с метаданными).

## Установка

Панель управления → Плагины → Репозитории → добавить:
`https://raw.githubusercontent.com/celestelsc21-cpu/jellyfin-plugin-rutracker/manifest/manifest.json`,
затем Каталог → RuTracker → Установить и перезапустить Jellyfin.

## Cloudflare

Если RuTracker показывает перед поиском страницу **«Just a moment…»**, сервер Jellyfin не может пройти JavaScript-проверку Cloudflare через `HttpClient`.

В настройках плагина предусмотрены поля **Cookie Cloudflare `cf_clearance`** и **User-Agent браузера**:

1. Откройте RuTracker в обычном браузере.
2. Пройдите проверку Cloudflare.
3. В инструментах разработчика откройте cookies для домена RuTracker и скопируйте значение `cf_clearance`.
4. Скопируйте User-Agent этого же браузера.
5. Вставьте оба значения в настройки плагина и сохраните их.
6. Нажмите «Проверить подключение».

`cf_clearance` связан Cloudflare с параметрами браузерной сессии, поэтому User-Agent должен соответствовать браузеру, который получил cookie. Cookie имеет ограниченный срок действия; при новом challenge её потребуется обновить.

Плагин **не пытается обходить JavaScript-проверку или капчу автоматически**: он использует clearance, полученный пользователем в реальном браузере.

## Кнопка в верхней панели

Плагин добавляет в веб-клиент кнопку RuTracker рядом с поиском (видна только
пользователям с ролью «Поиск»). Работает в браузере, Jellyfin Media Player и
приложении для Android; в приложениях для ТВ кнопки нет. Страница поиска
доступна и напрямую: `http://<сервер>:8096/RuTracker/Web/`.

## Требования

- Jellyfin Server **10.11.x** (собрано против 10.11.11, `targetAbi` 10.11.0.0).
- .NET SDK 9.0.200+ (нужен для `.slnx`).
- qBittorrent с включённым Web UI.
- Папки qBittorrent на Windows доступны контейнеру Jellyfin (SMB → bind-mount).

## Соответствие путей

qBittorrent пишет в `C:\video`, а Jellyfin видит ту же папку как `/data/video`.
Сделать её видимой в контейнере:

1. На Windows расшарьте `C:\video` (например, как `\\PC\video`).
2. На хосте смонтируйте шару, например в `/mnt/video`
   (`//PC/video /mnt/video cifs credentials=...,uid=<uid jellyfin>,iocharset=utf8 0 0` в `/etc/fstab`).
3. В `docker-compose.yml` Jellyfin добавьте в `volumes:` строку `- /mnt/video:/data/video`.
4. В настройках плагина задайте соответствие `/data/video ↔ C:\video`.
5. Добавьте папки загрузки в путях Jellyfin (`/data/video/Фильмы` и т.д.).

Кнопка «Проверить настройки» покажет, видит ли Jellyfin каждую папку и во что
она превращается для qBittorrent.

## Сборка

```bash
dotnet build -c Release
dotnet test
```

Через jprm (как в официальных репозиториях):

```bash
pip install jprm
jprm --verbosity=debug plugin build .
```

## Установка (Docker)

```bash
# каталог config смонтирован в контейнер как /config
mkdir -p /home/jellyfin/config/plugins/RuTracker_0.1.0.0
cp Jellyfin.Plugin.RuTracker/bin/Release/net9.0/Jellyfin.Plugin.RuTracker.dll \
   /home/jellyfin/config/plugins/RuTracker_0.1.0.0/
docker restart jellyfin
```

Логи: `docker logs jellyfin 2>&1 | grep -i rutracker` или файлы в
`/home/jellyfin/config/log/`.

## Роли

| Роль | Права |
|---|---|
| Администратор | всё, включая настройки |
| Скачивание | поиск + запуск загрузок + подписки |
| Поиск | поиск и список раздач |
| Остальные | ничего |

По умолчанию списки пусты, то есть доступ есть только у администраторов.

## API

| Метод | Доступ | Назначение |
|---|---|---|
| `GET /RuTracker/Access/Me` | любой вошедший | права текущего пользователя |
| `GET /RuTracker/Search?query=&kind=` | роль «Поиск» | поиск раздач (Movie / Series / Show); в ответе автор и признак субтитров |
| `GET /RuTracker/Targets` | роль «Скачивание» | папки загрузки (без путей) |
| `GET /RuTracker/Topics/{id}/Files` | роль «Скачивание» | видеофайлы раздачи в порядке просмотра |
| `POST /RuTracker/Downloads` | роль «Скачивание» | начать загрузку (`TopicId`, `Kind`, `TargetId`, `StartFileIndex`) |
| `GET /RuTracker/Downloads` | роль «Скачивание» | список загрузок с прогрессом |
| `GET /RuTracker/Downloads/{id}/Files` | роль «Скачивание» | серии загрузки с прогрессом |
| `POST /RuTracker/Downloads/{id}/Start` | роль «Скачивание» | смотреть с другой серии (`FileIndex`) |
| `DELETE /RuTracker/Downloads/{id}?deleteFiles=` | автор загрузки или администратор | отменить загрузку |
| `GET /RuTracker/Admin/Validate` | администратор | проверка настроек и путей |
| `POST /RuTracker/Admin/TestRuTracker` | администратор | пошаговая проверка RuTracker (оба адреса) |
| `POST /RuTracker/Admin/TestQBittorrent` | администратор | пошаговая проверка qBittorrent |
| `GET /RuTracker/Web/`, `/RuTracker/Web/header.js` | без входа | статические файлы страницы и кнопки (без данных) |

## Благодарности

Справочник разделов RuTracker и селекторы разбора выдачи основаны на индексаторе
RuTracker проекта [Jackett](https://github.com/Jackett/Jackett).
