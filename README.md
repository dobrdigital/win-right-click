# 🖱️ WIN.right.CLICK

**A simple app to add, edit, and remove entries in your Windows right-click menu — no registry editor needed.**

> 📥 **Quick start:** download the zip from [Releases](https://github.com/dobrdigital/win-right-click/releases),
> unzip it, and run `WIN.right.CLICK.exe`. That's the whole install — no setup wizard, nothing else to do.

Windows gives you no real way to edit the right-click menu yourself. The only
option is `regedit` — digging through several different, obscurely-named
registry locations, guessing what each cryptic entry actually does, and hoping
you don't break something in the process. Some entries can't even be
changed without administrator rights, because a program's installer put them
somewhere only admins can touch.

**WIN.right.CLICK** replaces all of that with one simple window. It shows
every right-click menu on your PC — desktop, folders, files, "Send to", and
extensions like 7-Zip — in one place, in plain language instead of cryptic
codes. You can add your own entries, edit or remove anyone's, and even the
"locked" (admin-only) ones unlock with a single permission prompt.

![Windows 8+](https://img.shields.io/badge/Windows-8%2B-0078D6)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)
![Version: 0.99 beta](https://img.shields.io/badge/version-0.99%20beta-purple)
![UI: Russian](https://img.shields.io/badge/UI-Russian-blue)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

---

## 🔥 Why you'll love it

| ❌ Without it | ✅ With WIN.right.CLICK |
|---|---|
| Edit the menu → open `regedit` → guess which of 5+ registry roots to touch | One GUI: Desktop / Folders / Files / Send To / Extensions, all in one place |
| A wall of CLSIDs — no idea which one is "Give access to" or your antivirus | Known Microsoft components resolved to real names, plus DLL/company info for the rest |
| Entries like `@shell32.dll,-8506` show up as raw gibberish | Resolved to the real localized text, exactly like Explorer shows it |
| A third-party installer wrote its entry into `HKEY_LOCAL_MACHINE` → permanently stuck | One UAC prompt unlocks **editing and deleting** HKLM entries for the rest of the session |
| Shell extensions (7-Zip, antivirus, etc.) can only be killed by uninstalling the whole program | Reversible enable/disable — the same "`-CLSID`" trick ShellExView uses |
| No idea what a mystery entry like `wsl.exe` or `RunAs` actually does | One click (**?**) looks it up for you |

## 🖥️ What it looks like

```
WIN.right.CLICK 0.99 beta
────────────────────────────────────────────────────────────────
 [ Рабочий стол ] [ Папки ] [ Файлы ] [ Отправить ] [ Расширения ]

  Название             Программа / путь                Тип        ⋯  ?
  ────────────────────────────────────────────────────────────────
  Tabby                C:\...\Tabby.exe                 Ваш        ⋯  ?
  Open PowerShell here powershell.exe                🔒 Общий(HKLM) ⋯  ?
  7-Zip                (COM shell extension)              —           ?

  [Добавить...] [Изменить] [Удалить] [Обновить] [Экспорт...] [Импорт...]
```

Every tab is a live, editable table — not a static viewer.

## 🧭 The 5 tabs

| Tab | What it manages | Backing store |
|---|---|---|
| **Рабочий стол** (Desktop) | Right-click on empty desktop | `DesktopBackground\shell`, `Directory\Background\shell` |
| **Папки** (Folders) | Right-click on a folder | `Directory\shell` |
| **Файлы** (Files) | Right-click on any file | `*\shell` |
| **Отправить** (Send To) | The "Send to" submenu | `%APPDATA%\Microsoft\Windows\SendTo` (plain shortcuts, not registry) |
| **Расширения** (Extensions) | COM shell extensions (7-Zip, "Give access to", antiviruses...) | `shellex\ContextMenuHandlers` across all scopes |

Every tab reads **both** `HKCU` and `HKLM` — so nothing is hidden, whoever
created it.

## ✨ Features

- 🖱️ **Full control** of Desktop / Folder / File / Send To / Extensions right-click menus, in one app.
- 🔍 **See everything real** — your own entries, other programs', and Windows' own, not just what this tool created.
- 🔒 **Elevated edit & delete for HKLM** ("common to all users") entries — one UAC prompt unlocks the rest of the session, not one prompt per click.
- 🧩 **Shell extensions toggle** — safe, reversible enable/disable, no uninstalling.
- 📂 **"⋯" jumps straight to the file's folder** (resolves bare names like `cmd.exe`/`wsl.exe` via PATH, exactly like Windows does).
- ❓ **"?" looks up any entry online** — paste-free, one click to DuckDuckGo.
- 🧠 **Decodes indirect strings** (`@shell32.dll,-8506` → the real text Explorer shows).
- 📦 **Export / import** your whole menu as JSON.
- 🔄 **Live auto-refresh** — the table updates the instant the registry changes, even from another program.
- 🌙 **Dark theme by default** — classic WinForms has no native dark mode, so every control is custom-themed.
- 🪶 **Single .exe, no installer** — .NET Framework 4.8, no external runtime to install on Windows 10/11.

## 🛠️ Install

> Requirements: Windows 8+, .NET Framework 4.8 (preinstalled on Windows 10/11).

**Option A — release zip:** download `WIN.right.CLICK-0.99-beta.zip` from the
[Releases](https://github.com/dobrdigital/win-right-click/releases) page, unzip, run
`WIN.right.CLICK.exe`. No installer.

**Option B — build from source** (needs the [.NET SDK](https://dotnet.microsoft.com/download)):

```bash
git clone https://github.com/dobrdigital/win-right-click
cd win-right-click
dotnet build -c Release
```

Run it from `bin\Release\net48\WIN.right.CLICK.exe`.

## 🔍 How the "locked" entries get unlocked

An entry registered in `HKEY_LOCAL_MACHINE` isn't necessarily "part of
Windows" — it's just registered **for every user of the machine**, which is
exactly what most installers do by default. WIN.right.CLICK can edit and
delete these too:

1. The first time you try, it asks to relaunch one small helper process as
   Administrator (a single UAC prompt).
2. That helper stays alive in the background for the rest of the session,
   listening on a private named pipe.
3. Every further HKLM change in that session — editing another entry,
   deleting one, toggling a shell extension — reuses the same helper.
   **No second UAC prompt.**
4. Closing the app closes the helper with it. Nothing lingers.

## 🔒 Safe & transparent

- **No telemetry, no background network calls.** The only thing that ever
  touches the network is the explicit **"?"** button, which opens *your*
  browser to a DuckDuckGo search — nothing is sent anywhere by the app itself.
- **Deletion is reversible where Windows allows it** — shell extensions are
  disabled with the same non-destructive prefix trick ShellExView uses, never
  deleted outright.
- **Every registry write is scoped precisely** to the one key you're
  editing — no bulk changes, no registry cleaning, no "optimize my PC" magic.
- Elevation is requested only for the specific action that needs it, never to
  run the app itself (it runs as a normal user by default).

## ⚠️ Known limitation

The vertical scrollbar stays the native Windows color on some builds — the
undocumented dark-mode API Windows itself uses for it doesn't always kick in
for third-party apps. Everything else is fully themed. Cosmetic only.

## 📦 Dependencies

.NET Framework 4.8 (runtime) + [`Svg`](https://github.com/svg-net/SVG) (NuGet,
only for rendering an optional logo). Nothing else.

## 📄 License

[MIT](LICENSE). Free to use, fork, and build on.

---

*Built with ❤ at **REAILISM.DEV** — because your right-click menu deserves better than regedit.*

---

<details>
<summary><b>🇷🇺 Русская документация</b></summary>

# 🖱️ WIN.right.CLICK

**Простая программа для добавления, редактирования и удаления пунктов меню правого клика — без редактора реестра.**

> 📥 **Быстрый старт:** скачайте zip со страницы
> [Releases](https://github.com/dobrdigital/win-right-click/releases), распакуйте и запустите
> `WIN.right.CLICK.exe`. Это весь «установочный процесс» — больше ничего делать не нужно.

Windows не даёт нормального способа отредактировать меню правого клика самому.
Единственный вариант — `regedit`: копаться по нескольким малопонятно
названным веткам реестра, гадать, что на самом деле делает тот или иной
пункт, и надеяться ничего не сломать. Некоторые пункты вообще нельзя
изменить без прав администратора — их записал туда установщик какой-то
программы.

**WIN.right.CLICK** заменяет всё это одним простым окном. Оно показывает все
меню правого клика на компьютере — рабочий стол, папки, файлы, «Отправить» и
расширения вроде 7-Zip — в одном месте и понятным языком, а не набором кодов.
Можно добавлять свои пункты, редактировать или удалять чужие, а
«заблокированные» (только для администратора) пункты разблокируются одним
запросом прав.

## Что внутри

- Полный контроль над меню рабочего стола / папок / файлов / «Отправить» / расширений — в одном приложении.
- Показывает **всё реальное**: свои пункты, чужих программ и Windows — не только созданное этим инструментом.
- **Права администратора для HKLM** запрашиваются один раз за сессию, а не на каждое действие.
- Безопасное обратимое включение/выключение COM-расширений (7-Zip, антивирусы и т.д.) — без удаления программы.
- Кнопка **«⋯»** открывает папку с файлом пункта меню (понимает и голые имена вроде `cmd.exe`/`wsl.exe` — так же, как это делает сама Windows).
- Кнопка **«?»** ищет информацию о любом пункте меню в DuckDuckGo одним кликом.
- Расшифровывает «непрямые строки» вида `@shell32.dll,-8506` в читаемый текст — так же, как это делает Проводник.
- Экспорт/импорт своего меню в JSON.
- Живое автообновление — список обновляется сразу при изменении реестра, даже из другой программы.
- Тёмная тема по умолчанию.
- Один .exe, без установщика — .NET Framework 4.8, никаких лишних зависимостей на Windows 10/11.

## Как разблокируются «системные» пункты

Пункт в `HKEY_LOCAL_MACHINE` — не обязательно часть Windows, это просто
регистрация «для всех пользователей компьютера», так делают почти все
инсталляторы. WIN.right.CLICK умеет редактировать и удалять и такие пункты:
при первой попытке один раз запрашиваются права администратора (один UAC), после
чего лёгкий фоновый процесс-помощник обслуживает все дальнейшие изменения HKLM
за эту сессию — без повторных запросов. При закрытии программы помощник
закрывается вместе с ней.

## Безопасность

Никакой телеметрии и фоновых сетевых запросов — единственное, что обращается
к сети, это явная кнопка **«?»**, которая открывает браузер с поиском в
DuckDuckGo. Расширения отключаются тем же безопасным обратимым способом, что
использует ShellExView (без удаления). Каждая запись в реестр затрагивает
только тот конкретный ключ, который вы редактируете — никакой «оптимизации»
и массовых изменений.

## Установка

Требования: Windows 8+, .NET Framework 4.8 (уже установлен в Windows 10/11).

**Вариант A — готовый архив:** скачайте `WIN.right.CLICK-0.99-beta.zip` со страницы
[Releases](https://github.com/dobrdigital/win-right-click/releases), распакуйте и
запустите `WIN.right.CLICK.exe`. Установщик не нужен.

**Вариант B — сборка из исходников** (нужен [.NET SDK](https://dotnet.microsoft.com/download)):

```bash
git clone https://github.com/dobrdigital/win-right-click
cd win-right-click
dotnet build -c Release
```

Готовый файл — `bin\Release\net48\WIN.right.CLICK.exe`.

Лицензия: [MIT](LICENSE). Сделано в **REAILISM.DEV**.

</details>
