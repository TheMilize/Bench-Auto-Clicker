<p align="center">
  <img src="BenchAutoClicker/Assets/bench-auto-clicker-icon.png" alt="Bench Auto Clicker" width="112" height="112">
</p>

# Bench Auto Clicker

Bench Auto Clicker is a self-contained, multi-mode auto clicker for Windows 10/11 with a Russian UI.

## Modes

- **Speed** — steady rate from 0.2 to 50 clicks per second.
- **Random interval** — one click after a random pause. Default range is 1 to 15 seconds.
- **Burst** — several fast clicks in a row, then a configurable pause.

In every mode you can choose the mouse button, click at the current cursor position or a saved point, add a small random position jitter, and set a delay before start.

The UI automatically switches between wide, compact, and phone-style layouts. Each layout has a safe minimum height: the selected mode, common settings, and start button always stay visible in the window with no hidden controls or scrolling.

## Quick start

1. Run `BenchAutoClicker.exe` from the `release\windows-x64` folder.
2. Choose a mode and configure options.
3. To click at a specific spot, move the cursor there and press **F7**.
4. Press **F6** to start. Press F6 again to stop.

Hotkeys work globally:

- **F6** — start or stop;
- **F7** — save the current cursor position;
- **Ctrl + Shift + F6** — emergency stop.

If `Ctrl + Shift + F6` is already taken by another app, Bench Auto Clicker automatically tries the fallback `Ctrl + Shift + F12` and shows the active key in the sidebar. Start is blocked until an emergency stop hotkey is available.

Clicks on the Bench Auto Clicker window itself are skipped automatically. The clicker also stops when the Windows session is locked or the PC goes to sleep. It does not require administrator rights and, per Windows rules, cannot click in windows running with higher privileges.

## Compatibility

| Platform | Status | Reason |
|---|---|---|
| Windows 10/11 x64 | Supported | Ready self-contained `win-x64` build |
| Linux x64 | Not supported natively | UI uses Windows Forms; input uses `user32.dll` |
| SteamOS / Steam Deck | Not supported natively | SteamOS is Linux-based; global clicks and hotkeys via Proton are not guaranteed |
| macOS Intel | Not supported | No `osx-x64` build or Core Graphics input implementation |
| macOS Apple Silicon | Not supported | No `osx-arm64` build or Core Graphics input implementation |

Test publishes for `linux-x64`, `osx-x64`, and `osx-arm64` fail because the project intentionally targets `net8.0-windows` and Windows Forms. A full port would need a cross-platform UI and separate click, global hotkey, power, and session-lock handling for each OS. Wine and Proton are not officially supported.

## Build from source

Requires .NET 8 SDK on Windows:

```powershell
dotnet build .\BenchAutoClicker\BenchAutoClicker.csproj -c Release
dotnet run --project .\BenchAutoClicker.Checks\BenchAutoClicker.Checks.csproj -c Release
dotnet publish .\BenchAutoClicker\BenchAutoClicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\release\windows-x64
```

Settings are stored in `%LOCALAPPDATA%\BenchAutoClicker\settings.json`.

## License

Distributed under the [MIT License](LICENSE).

---

# Bench Auto Clicker (RU)

Bench Auto Clicker — автономный многорежимный автокликер для Windows 10/11 с русским интерфейсом.

## Режимы

- **По скорости** — постоянный темп от 0,2 до 50 кликов в секунду.
- **Случайный интервал** — один клик после случайной паузы. По умолчанию диапазон составляет от 1 до 15 секунд.
- **Сериями** — несколько быстрых кликов подряд, затем настраиваемая пауза.

Во всех режимах можно выбрать кнопку мыши, кликать в текущей позиции курсора или в сохранённой точке, добавить небольшой случайный разброс позиции и настроить задержку перед запуском.

Интерфейс автоматически переключается между широким, компактным и телефонным расположением. Для каждого варианта задана безопасная минимальная высота: выбранный режим, общие настройки и кнопка запуска всегда остаются в окне одновременно, без скрытых элементов и прокрутки.

## Быстрый старт

1. Запустите `BenchAutoClicker.exe` из папки `release\windows-x64`.
2. Выберите режим и параметры.
3. Для кликов в определённом месте наведите туда курсор и нажмите **F7**.
4. Нажмите **F6** для запуска. Повторное нажатие F6 останавливает кликер.

Горячие клавиши работают глобально:

- **F6** — запуск или остановка;
- **F7** — сохранить текущую позицию курсора;
- **Ctrl + Shift + F6** — аварийная остановка.

Если `Ctrl + Shift + F6` уже занята другой программой, Bench Auto Clicker автоматически попробует резервную комбинацию `Ctrl + Shift + F12` и покажет фактическую клавишу в боковой панели. Без доступной аварийной комбинации запуск блокируется.

Клики по самому окну Bench Auto Clicker автоматически пропускаются. Кликер также останавливается при блокировке сеанса Windows и переходе компьютера в сон. Он не запрашивает права администратора и по правилам Windows не сможет кликать в окнах, запущенных с более высокими правами.

## Совместимость

| Платформа | Статус | Причина |
|---|---|---|
| Windows 10/11 x64 | Поддерживается | Готовая автономная сборка `win-x64` |
| Linux x64 | Не поддерживается нативно | Интерфейс использует Windows Forms, а ввод — `user32.dll` |
| SteamOS / Steam Deck | Не поддерживается нативно | SteamOS основана на Linux; работа глобальных кликов и хоткеев через Proton не гарантируется |
| macOS Intel | Не поддерживается | Нет сборки `osx-x64` и реализации ввода через Core Graphics |
| macOS Apple Silicon | Не поддерживается | Нет сборки `osx-arm64` и реализации ввода через Core Graphics |

Проверочные публикации для `linux-x64`, `osx-x64` и `osx-arm64` завершаются ошибкой, поскольку проект целенаправленно использует `net8.0-windows` и Windows Forms. Для полноценного переноса понадобится кроссплатформенный интерфейс и отдельные реализации кликов, глобальных горячих клавиш, управления питанием и блокировкой сеанса для каждой ОС. Wine и Proton официально не поддерживаются.

## Сборка из исходников

Требуется .NET 8 SDK на Windows:

```powershell
dotnet build .\BenchAutoClicker\BenchAutoClicker.csproj -c Release
dotnet run --project .\BenchAutoClicker.Checks\BenchAutoClicker.Checks.csproj -c Release
dotnet publish .\BenchAutoClicker\BenchAutoClicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\release\windows-x64
```

Настройки сохраняются в `%LOCALAPPDATA%\BenchAutoClicker\settings.json`.

## Лицензия

Проект распространяется по [лицензии MIT](LICENSE).
