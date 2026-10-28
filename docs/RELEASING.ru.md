# Выпуск мода: GitHub, сервер статистики, Steam Workshop

[English](RELEASING.md) · **Русский**

## 1. Подпись коммитов (значок «Verified» на GitHub)

Коммиты уже подписываются SSH-ключом `~/.ssh/id_ed25519` компьютера, на котором ведётся разработка. Чтобы GitHub
показывал «Verified», этот **публичный** ключ нужно один раз добавить в аккаунт как ключ подписи. Сделать это можно с
любого компьютера, в том числе с Windows, через браузер:

1. Откройте https://github.com/settings/ssh/new (войдите в аккаунт Akseleron).
2. **Title:** например `linux-pc signing`.
3. **Key type:** выберите **Signing Key** (не Authentication).
4. **Key:** вставьте строку целиком:
   ```
   ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIyLvK1XB0J4hrkDEukSU0YIoT2AzBJqHMgwa+N+SqSK
   ```
5. Нажмите **Add SSH key**. Старые подписанные коммиты тоже станут «Verified».

Это публичная часть ключа, её безопасно публиковать. Приватный ключ остаётся на компьютере и никуда не передаётся.
Тот же результат даёт команда `gh ssh-key add ~/.ssh/id_ed25519.pub --type signing` на Linux-компьютере. Перед ней
нужно выполнить `gh auth refresh -s admin:ssh_signing_key` и подтвердить вход в браузере.

## 2. Сервер статистики на Cloudflare (бесплатно)

Бесплатный тариф Workers: 100 000 запросов в сутки, KV — 100 000 чтений и 1 000 записей в сутки. Мод отправляет отчёт
не чаще раза в 30 минут на игрока, а сервер дополнительно ограничивает частоту, поэтому лимита хватит на тысячи
игроков. Банковская карта не нужна.

**Что подготовить:**
- аккаунт Cloudflare: https://dash.cloudflare.com/sign-up (только email и пароль);
- Node.js LTS (на Windows: https://nodejs.org → установщик LTS);
- GitHub-токен, чтобы сервер мог заводить issue:
  1. Откройте https://github.com/settings/personal-access-tokens/new.
  2. **Name:** `agent-bridge-telemetry`, **Expiration:** до 1 года.
  3. **Repository access:** Only select repositories → `rimworld-agent-bridge`.
  4. **Permissions → Repository → Issues:** Read and write.
  5. **Generate token**, скопируйте его — он показывается один раз.

**Развёртывание через браузер (без Node.js):**
1. Storage & databases → **Workers KV** → Create → имя `agent-bridge-stats`.
2. Compute → **Workers & Pages** → Create → **Start with Hello World** → имя `agent-bridge-telemetry` → Deploy.
3. **Edit code** → удалить всё → вставить https://raw.githubusercontent.com/Akseleron/rimworld-agent-bridge/main/telemetry/worker.js → Deploy.
4. Воркер → Settings → **Bindings** → Add → KV namespace: Variable name `STATS`, namespace `agent-bridge-stats`.
5. Settings → **Variables and Secrets** → Add:
   - Type *Secret*, name `GITHUB_TOKEN`, value — GitHub-токен;
   - Type *Text*, name `REPO`, value `Akseleron/rimworld-agent-bridge`.
6. Адрес воркера — на его странице: `https://agent-bridge-telemetry.<имя>.workers.dev`.

**Развёртывание из терминала** (если Node.js работает):

```
npx wrangler login                                   # откроет браузер, войти в Cloudflare
npm create cloudflare@latest agent-bridge-telemetry  # Hello World example → Worker only → JavaScript → git: no → deploy: no
cd agent-bridge-telemetry
npx wrangler kv namespace create STATS               # выведет id и готовый блок привязки
```

1. Откройте `wrangler.jsonc` (или `wrangler.toml`) и добавьте привязку, которую вывела команда: `kv_namespaces` с
   `binding = "STATS"` и полученным `id`.
2. Замените содержимое `src/index.js` на `telemetry/worker.js` из этого репозитория.
3. Задайте секреты и разверните:

```
npx wrangler secret put GITHUB_TOKEN                 # вставить токен
npx wrangler secret put REPO                         # Akseleron/rimworld-agent-bridge
npx wrangler deploy                                  # выведет адрес https://agent-bridge-telemetry.<имя>.workers.dev
```

4. В репозитории создайте метки `auto-report` и `bug` (Issues → Labels), если их нет.
5. Отправьте адрес `https://…workers.dev/v1/report` разработчику (агенту). Его впишут в `Telemetry.DefaultEndpoint`,
   и сбор заработает в следующем релизе.

**Проверка:** в дашборде Cloudflare → Workers → agent-bridge-telemetry → Logs видны запросы. В KV появятся ключи
`day:…`, а при ошибках — issue с меткой `auto-report`.

## 3. Первый выпуск в Steam Workshop

Что нужно один раз:
- RimWorld, купленный в Steam, и Steam, запущенный под тем же аккаунтом;
- принятое соглашение Steam Workshop. Steam попросит об этом при первой загрузке; если предмет не виден другим, откройте
  страницу предмета и примите соглашение (https://steamcommunity.com/sharedfiles/workshoplegalagreement);
- картинка `About/Preview.png`: PNG до 1 МБ, рекомендуется 640×360 (16:9) — её видят в Мастерской и в списке модов.

Порядок:
1. Соберите и проверьте: `cd Source && dotnet build -c Release`, затем smoke-тест в игре.
2. `python Tools/make_release.py` — создаст чистую папку `../AgentBridge-release` без `.git`, `Source`, `bin`.
3. Закройте игру. Убедитесь, что в `RimWorld/Mods` установлена только одна копия Agent Bridge. Например, временно
   направьте ссылку `Mods/AgentBridge` на папку релиза или скопируйте релиз вместо разработческой папки.
4. Запустите RimWorld. Включите режим разработчика: Настройки → Режим разработчика.
5. Главное меню → **Моды** → выберите **Agent Bridge** → кнопка **«Загрузить в Steam Workshop»** (видна только для
   локальных модов в режиме разработчика) → подтвердите. Игра загрузит папку и создаст `About/PublishedFileId.txt`.
6. Откройте страницу предмета в Steam (Мастерская → Ваши файлы):
   - проверьте видимость (сначала можно оставить «Только для друзей» или «Скрытый» и проверить подписку со второго
     компьютера);
   - добавьте описание на английском и русском (текст из About.xml / README), ссылку на GitHub и раздел «Приватность»;
   - при желании добавьте скриншоты.
7. Скопируйте `PublishedFileId.txt` в `About/` репозитория и закоммитьте. Тогда следующие загрузки обновят тот же
   предмет, а не создадут новый.
8. Сделайте на GitHub релиз с тегом `v0.4.0` и zip-архивом папки релиза.

**Обновления:** поднять версию → собрать → `make_release.py` → та же кнопка «Загрузить в Steam Workshop» (в окне можно
указать заметку об изменениях) → релиз на GitHub.

**Важно для страницы Мастерской:**
- инструменты для агентов (`Tools/*.py`) требуют установленного Python 3; без него мод работает, но подключаться
  придётся напрямую по HTTP;
- после подписки мод лежит в `steamapps/workshop/content/294100/<id>/` — команду MCP с правильным путём игрок копирует
  кнопкой в настройках мода;
- честно укажите, что статистика анонимная, включена по умолчанию и выключается в настройках (ссылка на PRIVACY).

## 4. Чек-лист перед первым релизом

- [ ] `About/Preview.png` (640×360, < 1 МБ)
- [ ] проверка на Windows: подписка или копия мода, запуск, `python Tools/rwb.py snapshot`
- [ ] (по желанию) сервер статистики развёрнут, адрес вписан в `Telemetry.DefaultEndpoint`
- [ ] версия 0.4.0 → 1.0.0 (или оставить 0.x как «бета»)
- [ ] описание и скриншоты на странице Мастерской
- [ ] GitHub Release с zip-архивом
