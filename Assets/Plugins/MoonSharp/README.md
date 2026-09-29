# MoonSharp

Lua interpreter для встроенного скриптинга SRG (диалоги, дебаг-консоль, квесты, моды).

## Что здесь

- `MoonSharp.Interpreter.dll` — интерпретатор (variant `net40` из официального релиза).
- `LICENSE.txt` — оригинальный текст лицензии MoonSharp (BSD-3-Clause).

## Версия

- Release: **v2.0.0.0**
- Источник: https://github.com/moonsharp-devs/moonsharp/releases/tag/v2.0.0.0
- Файл в релизе: `interpreter/net40/MoonSharp.Interpreter.dll`
- SHA-256 архива:  `195706afcffc35df7852b67950576ca3b295e7ee02577c3c534dc900eee8989b`
- SHA-256 DLL:     `1db76110f21698639f55d28e21bddb536c0c497ceb741dee49fedcca9bcd1588`

## Почему net40

Проект собирается с `apiCompatibilityLevel = .NET Standard 2.1` (Unity 6). Mono/IL2CPP-рантаймы Unity полностью покрывают .NET Framework 4.x API, а `net40`-вариант — самый переносимый среди «десктопных» бинарей MoonSharp: без зависимостей от старых netcore/PCL контрактов, без editor-only кода.

## Как обновить

1. Скачать новый релиз с https://github.com/moonsharp-devs/moonsharp/releases.
2. Заменить `MoonSharp.Interpreter.dll` файлом из `interpreter/net40/` (или `netcore/`, если net40 в новом релизе отсутствует).
3. Обновить `LICENSE.txt`, если апстрим-лицензия менялась.
4. Обновить версию и хеши в этом README.

## Лицензия

MoonSharp распространяется под BSD-3-Clause, см. `LICENSE.txt`. Обязательства проекта:

- сохранить `LICENSE.txt` в дистрибутиве (лежит рядом с DLL — попадает в билд как plugin asset);
- атрибуция также вынесена в `docs/third_party_licenses.md`.
