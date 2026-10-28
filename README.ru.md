# Agent Bridge для RimWorld

[English](README.md) · **Русский**

Управление RimWorld 1.7 из ИИ-агентов и скриптов (Claude Code, Codex, любой MCP-клиент или обычный HTTP)
JSON-командами вместо скриншотов и координат мыши.

```
$ python Tools/rwb.py snapshot fields='["colonists"]'
{"ok":true,"result":{"colonists":[{"id":"Human1551","name":"Austin","pos":[45,109],"health":1,"job":"Wait_Wander","mood":1}, ...]}}
$ python Tools/rwb.py draft ids='["Human1554"]'
$ python Tools/rwb.py gizmo id=Human1554 key=verb:Gun_Revolver target=Pony_Griffon26825
$ python Tools/rwb.py tick n=600
{"ok":true,"result":{"tick":2655,"ran":600}}
```

## Возможности

- **Около 90 команд:**
  - наблюдение: `snapshot`, `pawns`, `pawn`, `things`, `cell`, текстовая карта `area`, `alerts`, `letters`, `windows`;
  - приказы: `draft`, `move`, `attack`, `gizmo` + `target`, меню правого клика `menu`/`menu-choose`, `designate`,
    `build`, `zone`, `bills`, `work`, `schedule`, `equip`;
  - диалоги и письма, `save`/`load`;
  - точное управление временем: `tick n=…` останавливается ровно на нужном тике, `wait until=death|letter|log…`
    ждёт события;
  - любое действие меню разработчика (`debug path=…`);
  - правка мира: `spawn`, `incident`, `damage`, `heal`…;
  - инструменты моддера: живая рефлексия `obj`, `patches`, C# через `eval`.
- **Экономия токенов:** компактный JSON; поля со значением null и false опускаются; rich-text теги вырезаются.
  Проекция `fields:[…]` оставляет только нужные поля, `snapshot` даёт обзор одним вызовом, `batch` выполняет несколько
  команд за кадр, соединения переиспользуются. Команда занимает один кадр (~17 мс).
- **Не зависит от языка:** команды используют defName, ThingID и стабильные ключи гизмо (`key:Command_ColonistDraft`).
- **Без обязательных зависимостей.** Harmony (если установлен — а он есть почти в любой сборке) добавляет наведение на
  клетку для debug-инструментов и меню, точную остановку тиков и перехват событий. Без него мост работает с урезанными
  возможностями.
- **Расширяется другими модами** без жёсткой зависимости (см. [docs/EXTENDING.ru.md](docs/EXTENDING.ru.md)).
- **Игрок под контролем:** в настройках мода видны подключённые агенты и последние команды, есть режим «только чтение» и
  уровни доступа. Пока агент действует, на экране горит индикатор. Сервер доступен только с 127.0.0.1 и требует
  случайный токен.
- **Кроссплатформенность:** одна сборка .NET 10 для RimWorld 1.7 на Linux, Windows и macOS. На Windows и macOS вживую
  пока не проверялась — будем рады отчётам.
- **Переведён** на все 29 языков, которые входят в RimWorld.

## Установка

1. Подпишитесь в Steam Workshop (скоро) или скопируйте эту папку в `RimWorld/Mods/AgentBridge`.
2. Включите **Agent Bridge** (где угодно после Harmony, если он у вас есть).
3. Запустите игру. В логе появится `[AgentBridge] … listening on 127.0.0.1:8765`.

Данные подключения записываются в файл, доступный только вам:
`~/.local/state/rimworld-agent-bridge/descriptor.json` (Linux/macOS) или `%LOCALAPPDATA%\AgentBridge\descriptor.json`
(Windows). Инструменты читают его сами.

## Подключение агентов

**MCP** (Claude Code, Codex, другие клиенты). Точная команда есть и в *Настройки модов → Agent Bridge → Скопировать
команду настройки MCP*:

```
claude mcp add rimworld -- python3 "<RimWorld>/Mods/AgentBridge/Tools/rwb_mcp.py"
codex mcp add rimworld -- python3 "<RimWorld>/Mods/AgentBridge/Tools/rwb_mcp.py"
```

Инструменты:
- `rimworld` — любая команда;
- `rimworld_eval` — C# внутри игры;
- `rimworld_source` — декомпиляция типов игры и модов;
- `rimworld_defs` — поиск XML Defs;
- `rimworld_log` — чтение Player.log, работает и после краша.

**CLI:** `python Tools/rwb.py <cmd> ключ=значение …` (значения разбираются как JSON, если возможно).
Список команд — `python Tools/rwb.py help`.

**HTTP:** `POST http://127.0.0.1:8765/` с заголовком `X-Token: <token>` и телом `{"cmd":"pawns","filter":"hostile"}`.
Подробности протокола — в [docs/PROTOCOL.ru.md](docs/PROTOCOL.ru.md).

Начинайте с `snapshot`; `help` выводит команды вместе с уровнем доступа.

## Настройки

Настройки → Настройки модов → Agent Bridge:
- включение и выключение сервера, порт;
- режим «только чтение»;
- разрешение dev-команд и мощных команд (eval, рефлексия), требование режима разработчика для обоих (по умолчанию включено);
- индикатор на экране;
- подключённые агенты и последние команды;
- копирование команды MCP, новый токен (отключает всех агентов);
- статистика и отчёты об ошибках, кнопка «Сообщить об ошибке».

## Приватность

Анонимная статистика и отчёты об ошибках включены по умолчанию. При первом запуске мод один раз спрашивает согласие,
выключить их можно в любой момент. Что именно собирается — в [PRIVACY.ru.md](PRIVACY.ru.md). Кнопка «Сообщить об ошибке» открывает заполненный issue на GitHub.

## Сборка

```
cd Source && dotnet build -c Release     # Source/bin/AgentBridge.dll, копируется в 1.7/Assemblies/
dotnet build -c Release -p:SkipModCopy=true   # пока RimWorld запущен: нельзя перезаписывать загруженную сборку
```

Референсные сборки берутся из NuGet (`Krafs.Rimworld.Ref`, `Lib.Harmony`), поэтому установленная игра для сборки не нужна.
`python Tools/rwb_smoke.py` прогоняет живой тест ~70 команд на одноразовой колонии (`rwb.py quicktest`).
Выпуск в Steam Workshop — [docs/RELEASING.ru.md](docs/RELEASING.ru.md).

## Лицензия

MIT. RimWorld © Ludeon Studios; это неофициальный мод.
