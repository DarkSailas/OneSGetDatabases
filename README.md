# 1С: Get Databases (v1.5.0) — Инвентаризация баз 1С, инспекция СУБД и автосинхронизация Confluence

[![Version](https://img.shields.io/badge/version-1.5.0-blue.svg)](https://github.com/DarkSailas/OneSGetDatabases)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![C# 14](https://img.shields.io/badge/C%23-14.0-239120?style=flat&logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20Service%20%7C%20Web-blue)](https://microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Высокопроизводительный инженерный комплекс на стеке **.NET 10 / C# 14** для сквозной инвентаризации распределенной инфраструктуры **1С:Предприятие 8.3**, глубокой инспекции серверов СУБД (**Microsoft SQL Server / PostgreSQL**), аудита прав **Active Directory** и автоматической синхронизации документации в **Atlassian Confluence**.

---

## 📌 Зачем создан этот проект

В корпоративных ландшафтах с десятками серверов приложений 1С, сотнями кластеров (PROD, DEV, TEST, QA), распределенными фермами баз данных и тысячами пользователей в Active Directory поддержка актуальной карты информационных баз вручную становится практически невозможной. 

**OneSGetDatabases решает эту задачу целиком:**
* Автоматически сканирует все кластеры 1С и собирает метаданные баз через встроенные механизмы платформы (`rac.exe` / RAS).
* Напрямую опрашивает серверы СУБД, определяя реальные физические пути к файлам данных (`MDF`/`NDF`), журналам транзакций (`LDF`), их точные размеры и список учетных записей СУБД.
* Сопоставляет информационные базы с группами безопасности домена Active Directory (`rdp_1c_*`, `1cbases_*`), позволяя мгновенно увидеть состав допущенных пользователей с автоматической фильтрацией вложенных групп.
* Автоматически публикует структурированные таблицы с цветовой индикацией в базу знаний Atlassian Confluence по регламентному расписанию.
* Предоставляет быстрый веб-интерфейс (Kestrel SPA Dashboard) для оперативного мониторинга и выгрузки отчетов в Excel/JSON.

---

## 🏗️ Архитектура и стек технологий

```
OneSGetDatabases/
├── src/
│   ├── OneSGetDatabases.Core/      # Ядро: RAC/RAS, СУБД инспекция, LDAP AD, Confluence REST, Кэш
│   └── OneSGetDatabases.Web/       # Веб-панель (Kestrel SPA) + служба Windows Service (порт 5070)
├── tests/
│   └── OneSGetDatabases.Tests/     # Юнит-тесты xUnit / FluentAssertions / Moq
├── build.ps1                       # Скрипт сборки, тестирования и публикации
├── Directory.Build.props           # Глобальные настройки сборки .NET 10
└── LICENSE                         # MIT License
```

* **Core Engine**: .NET 10.0 (C# 14), Zero sync-over-async (`async`/`await`, `ValueTask`), минимизация аллокаций.
* **Web & Service**: Гибридное приложение ASP.NET Core Minimal APIs + Windows Service (`Microsoft.Extensions.Hosting.WindowsServices`) на порту `5070`.
* **Frontend**: Ультра-быстрый SPA (Vanilla JS + CSS3 Modern Dark Theme) с авто-ресайзом колонок, сквозным поиском и адаптивной высотой окон.
* **Инспекция СУБД**: `Microsoft.Data.SqlClient` и `Npgsql` (чтение `sys.master_files`, `sys.databases`, `sys.database_principals`, `pg_database_size`).
* **Active Directory**: `System.DirectoryServices` (LDAP) с пакетным чтением атрибутов, распознаванием активности пользователей и фильтрацией вложенных групп.
* **Хранение состояния**: Многоуровневое кэширование (In-Memory + Thread-Safe Persistent Disk Cache) для мгновенного отклика (< 5 мс) даже при недоступности внешних серверов.

---

## ✨ Ключевые возможности

### 1. Параллельный сбор данных из кластеров 1С
- Многопоточный опрос сотен кластеров через пул процессов `rac.exe`.
- Определение версии платформы 1С, режима блокировки регламентных заданий, параметров сеансов и описания баз.
- Fallback-механизм: если сервер СУБД не указан в 1С, адрес резолвится через сервисы Consul или прямой индекс баз данных.

### 2. Глубокая инспекция СУБД (MS SQL / PostgreSQL)
- Точные размеры файлов данных и журналов транзакций в мегабайтах и гигабайтах.
- Отображение физических путей на дисках сервера СУБД.
- Модель восстановления (`FULL`, `SIMPLE`, `BULK_LOGGED`), уровень совместимости и статус (`ONLINE`/`OFFLINE`).
- Список пользователей СУБД и их назначенных ролей.

### 3. Аудит доступа и интеграция с Active Directory
- Автоматический поиск привязанных групп безопасности (RemoteApp, RDP, прямой доступ 1С).
- Просмотр состава участников группы в один клик: ФИО, логин (SAM), должность, отдел, рабочий e-mail и статус учетной записи (активна/отключена).
- Корректная обработка вложенных групп домена (Group Nesting) с отображением только физических учетных записей.

### 4. Автоматическая выгрузка в Atlassian Confluence
- Генерация валидного XHTML с макросами статусов (`macro-status`), панелями предупреждений и табличной разметкой.
- Раздельные регламентные страницы для PROD, DEV и сводной инфраструктурной таблицы (SA Info).
- Отправка email-уведомлений администраторам в случае ошибок публикации.

### 5. Интерактивная веб-панель управления (Web Dashboard)
- Таблица информационных баз 1С и таблица файлов/размеров СУБД с пагинацией «Все строки» по умолчанию.
- Мгновенная сортировка по любой колонке, перетаскивание границ столбцов (drag-resize).
- Множественный выбор строк (чекбоксы) с плавающей панелью действий: экспорт в Excel/JSON, копирование в буфер обмена.
- Адаптивные модальные окна участников AD, подстраивающиеся под количество строк без пустых зон.
- Диалоговое подтверждение запуска полного сканирования с анимацией очистки и сбора данных в реальном времени.

### 6. Состояние и диагностика кластеров 1С (Cluster Health)
- Интерактивное окно состояния кластеров 1С со сквозной фильтрацией по статусам («Все», «Онлайн», «Пустые», «Недоступны», «Логи опроса»).
- Подробный аудит этапов обнаружения, версий платформы и связок с агентами RAS.
- Встроенный просмотр логов опроса с экспортом детального диагностического отчета в CSV.

---

## 🚀 Быстрый старт

### Требования
* **ОС**: Windows Server 2016 / 2019 / 2022 / Windows 10/11 x64
* **Среда выполнения**: [.NET 10.0 Runtime / Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0)
* **Платформа 1С**: Установленная платформа 1С:Предприятие 8.3 (наличие `rac.exe`)

### 1. Сборка проекта
Для сборки решения и прогона всех тестов выполните в PowerShell:
```powershell
.\build.ps1
```
Готовый к развертыванию бинарный пакет будет опубликован в каталог `publish\Web`.

### 2. Конфигурация (`appsettings.json`)
Отредактируйте файл `publish\Web\appsettings.json` под вашу инфраструктуру:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://*:5070" }
    }
  },
  "Scheduler": {
    "DiscoveryIntervalMinutes": 30,
    "EnableAutoSyncConfluence": true,
    "ConfluenceSyncTime": "04:00",
    "ConfluenceSyncIntervalHours": 24
  },
  "Rac": {
    "RacPath": "C:\\Program Files\\1cv8\\8.3.25.1445\\bin\\rac.exe",
    "TimeoutSeconds": 15,
    "MaxConcurrency": 16
  },
  "ActiveDirectory": {
    "Domain": "example.corp",
    "Username": "svc_1c_inventory",
    "Password": "YourSecureAdPassword123!",
    "GroupFilters": [ "rdp_1c_*", "1cbases_*" ],
    "V8iBasePath": "\\\\fileserver.example.corp\\1c_bases"
  },
  "Dbms": {
    "DefaultSqlUsername": "monitoring_user",
    "DefaultSqlPassword": "YourSecureDbmsPassword123!",
    "DefaultPgUsername": "monitoring_user",
    "DefaultPgPassword": "YourSecureDbmsPassword123!",
    "ConnectionTimeoutSeconds": 8
  },
  "Confluence": {
    "BaseUrl": "https://confluence.example.com/rest/api/content/{0}",
    "BearerToken": "YOUR_CONFLUENCE_API_TOKEN_HERE",
    "PageIdDev": "10000001",
    "PageIdProd": "10000002",
    "PageIdSaInfo": "10000003"
  },
  "ClusterDiscovery": {
    "Enabled": true,
    "DefaultClusterUser": "admin_1c",
    "DefaultClusterPassword": "YourClusterPassword123!",
    "Servers": [
      { "Host": "app-dev01.example.corp", "Environment": "DEV" },
      { "Host": "app-prod01.example.corp", "Environment": "PROD" }
    ]
  },
  "AuditLog": {
    "RetentionDays": 14,
    "MaxLogSizeBytes": 1073741824,
    "LogFilePath": "logs/audit.jsonl"
  },
  "Clusters": []
}
```

### 3. Секретная инженерная консоль (Easter Egg Admin Console)
Для дежурных инженеров и администраторов предусмотрен скрытый интерфейс управления, изолированный в рамках текущего браузерного сеанса (не отображается рядовым пользователям):
* **Управление службами 1С**: Активируется комбинацией `Ctrl + Alt + Клик` по заголовку *«1С: Get Databases»*. 
  * Отображает актуальный статус всех служб `ragent` и `ras` с официальными русскоязычными именами, учетными записями запуска и путями к `srvinfo`.
  * Интеллектуальные кнопки управления: **Старт** (активна, если служба остановлена), **Стоп**, **Перезапуск** и **Перезапуск с очисткой кэша**.
  * Каждое действие требует подтверждения в модальном окне.
  * **Безопасная очистка кэша**: принудительно удаляет **исключительно** каталоги сессионных контекстов `snccntx*`, гарантированно сохраняя файлы настроек кластера (`1CV8Clst.lst`, `1cv8ws.lst`) и конфигурации информационных баз.
* **Журнал аудита действий**: Активируется комбинацией `Ctrl + Alt + Клик` по бейджу версии в правом нижнем углу.
  * Фиксирует IP-адрес инициатора, целевой сервер, порт кластера, выполненное действие, статус (`SUCCESS` / `FAILED`), длительность и сообщение об ошибке.
  * Автоматическая ротация: хранение 14 дней (настраивается) и ограничение размера файла до 1 ГБ.

### 4. Установка в качестве службы Windows
Запустите PowerShell от имени администратора в каталоге публикации:
```powershell
cd .\publish\Web
.\INSTALL_SERVICE.ps1
```
Служба `OneSGetDatabasesWeb` будет создана с автозапуском и стартует веб-интерфейс на `http://localhost:5070`.

---

## 🔒 Безопасность и права доступа

* **SQL Server**: Сервисной учетной записи не требуются административные права `sysadmin`. Достаточно выдать права `VIEW ANY DATABASE`, `VIEW SERVER STATE`, `VIEW ANY DEFINITION` и `CONNECT ANY DATABASE`.
* **Active Directory**: Учетная запись службы запускается в режиме только для чтения (достаточно членства в `Windows Authorization Access Group`).
* **Управление службами 1С**: WMI-вызовы `StartService` / `StopService` выполняются под контекстом сервисного аккаунта приложения (требуются права локального администратора на целевых хостах 1С).
* **Web Dashboard**: Веб-интерфейс содержит встроенные механизмы санитизации входных данных и экранирования вывода (защита от XSS и SQL Injection).


---

## 📄 Лицензия

Проект распространяется под свободной лицензией **MIT License**. Вы можете использовать и модифицировать его под нужды вашей компании.
