# Сервер-получатель телеметрии

[English](README.md) · **Русский**

Какие варианты сбора отчётов от игроков рассматривались:

| Вариант | Вывод |
|---|---|
| Steam Workshop | у Steam нет API, через который мод мог бы передавать данные; комментарии пишутся только вручную |
| GitHub Issues прямо из игры | в мод пришлось бы положить токен с правом записи — любой мог бы его извлечь и злоупотребить |
| **Небольшой serverless-получатель** (эта папка) | токен остаётся на сервере, есть лимит частоты и удаление дублей ошибок, бесплатного тарифа хватает |
| Отчёт по инициативе игрока | кнопка «Сообщить об ошибке»: заполненный issue на GitHub + буфер обмена; работает без сервера |

## Развёртывание (Cloudflare Workers, бесплатный тариф)

Нужны бесплатный аккаунт Cloudflare и Node.js LTS. Пошагово (в том числе для Windows) — в
[docs/RELEASING.ru.md](../docs/RELEASING.ru.md#2-сервер-статистики-на-cloudflare-бесплатно).

```
npm create cloudflare@latest agent-bridge-telemetry   # шаблон "Hello World" Worker, код заменить на worker.js
npx wrangler kv namespace create STATS                 # добавить привязку в wrangler.toml / wrangler.jsonc
npx wrangler secret put GITHUB_TOKEN                   # fine-grained токен: этот репозиторий, Issues read/write
npx wrangler secret put REPO                           # Akseleron/rimworld-agent-bridge
npx wrangler deploy
```

После этого впишите в `Telemetry.DefaultEndpoint` адрес `https://<worker>.workers.dev/v1/report` и выпустите новую
версию. До этого мод ничего не отправляет.

Суточные сводки хранятся в KV под ключами `day:<дата>:<версия>`. Каждая новая сигнатура ошибки становится одним issue с
меткой `auto-report`; повторы только увеличивают её счётчик.
