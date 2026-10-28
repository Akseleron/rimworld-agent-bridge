# Материалы для Steam Workshop

- `description.bbcode` — текст страницы (английский + русский раздел). Вставляется в поле описания на странице предмета
  в Steam (Мастерская → предмет → «Изменить название и описание»).
- `../media/1-orders.png`, `2-map.png`, `3-control.png` — скриншоты 1920×1080: страница предмета → «Добавить/изменить
  изображения и видео». Пересобрать: `python docs/media/make_screens.py` (исходные кадры игры — `docs/media/src/`).
- `../../About/Preview.png` — обложка (из `About/Preview.svg`), загружается игрой вместе с модом.
- `mockup.png` — примерный вид страницы (`python docs/steam/mockup.py`).
