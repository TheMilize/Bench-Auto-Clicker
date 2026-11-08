using System.Globalization;
using System.Runtime.CompilerServices;

namespace BenchAutoClicker;

internal static class AppStrings
{
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;
    private static readonly CultureInfo SystemNumberCulture = CultureInfo.CurrentCulture;
    private static readonly ConditionalWeakTable<Control, TextBinding> TextBindings = new();
    private static readonly ConditionalWeakTable<Control, TipBinding> TipBindings = new();
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        [" мс"] = " ms",
        [" с"] = " s",
        [" кликов"] = " clicks",
        ["Левая кнопка"] = "Left button",
        ["Правая кнопка"] = "Right button",
        ["Средняя кнопка"] = "Middle button",
        ["Двойной левый"] = "Double left",
        ["За курсором"] = "Follow cursor",
        ["Фиксированная"] = "Fixed point",
        ["Поверх окон"] = "Always on top",
        ["Скорость"] = "Speed",
        ["Случайно"] = "Random",
        ["Серии"] = "Burst",
        ["АВТОМАТИЗАЦИЯ ПОД КОНТРОЛЕМ · v2.0"] = "AUTOMATION UNDER CONTROL · v2.1",
        ["Скорость, CPS"] = "Speed, CPS",
        ["Интервал, мс"] = "Interval, ms",
        ["Пресет"] = "Preset",
        ["1–15 с"] = "1–15 s",
        ["3–8 с"] = "3–8 s",
        ["0,5–2 с"] = "0.5–2 s",
        ["1 CPS · Тихо"] = "1 CPS · Slow",
        ["5 CPS · Баланс"] = "5 CPS · Balanced",
        ["20 CPS · Быстро"] = "20 CPS · Fast",
        ["0 — без лимита. Двойной клик учитывается как два физических клика."] = "0 means unlimited. A double-click counts as two physical clicks.",
        ["0 — без лимита. Время отсчитывается после задержки запуска."] = "0 means unlimited. Session time starts after the launch countdown.",
        ["Точный интервал между кликами. Ввод с клавиатуры или перетаскивание дорожки."] = "Exact interval between clicks. Type a value or drag the track.",
        ["F7 сохраняет позицию курсора. Без точки используется центр экрана."] = "F7 saves the cursor position. Without a saved point, the screen center is used.",
        ["Минимум, сек"] = "Minimum, s",
        ["Максимум, сек"] = "Maximum, s",
        ["Кликов"] = "Clicks",
        ["Пауза, сек"] = "Pause, s",
        ["Настройки сессии"] = "Session settings",
        ["Центр экрана · F7"] = "Screen center · F7",
        ["Сбросить точку и использовать центр экрана"] = "Reset point and use the screen center",
        ["Сбросить точку — использовать центр экрана под курсором"] = "Reset point: use the center of the screen under the cursor",
        ["Кнопка мыши"] = "Mouse button",
        ["Позиция клика"] = "Click position",
        ["Цель · F7"] = "Target · F7",
        ["Разброс, px"] = "Jitter, px",
        ["Перед стартом, с"] = "Start delay, s",
        ["Лимит кликов"] = "Click limit",
        ["Лимит времени, с"] = "Time limit, s",
        ["Всегда на виду"] = "Window behavior",
        ["КЛИКОВ"] = "CLICKS",
        ["Всё готово"] = "Ready",
        ["F6 — запуск · F7 — точка"] = "F6 — start · F7 — point",
        ["Лимит кликов выполнен"] = "Click limit reached",
        ["Лимит времени выполнен"] = "Time limit reached",
        ["Случайный интервал"] = "Random interval",
        ["Один клик после случайной паузы"] = "One click after a random pause",
        ["Клики сериями"] = "Burst clicks",
        ["Короткая серия → пауза → повтор"] = "Short burst → pause → repeat",
        ["Постоянная скорость"] = "Constant speed",
        ["Ровный темп от 0,2 до 50 кликов в секунду"] = "Steady rate from 0.2 to 50 clicks per second",
        ["Остановлено пользователем"] = "Stopped by user",
        ["Переключитесь в нужное окно"] = "Switch to your target window",
        ["Кликер уже работает."] = "The clicker is already running.",
        ["Планируется первый клик…"] = "Scheduling the first click…",
        ["Запуск отменён"] = "Start cancelled",
        ["Защита от кликов по Bench Auto Clicker включена"] = "Clicks on Bench Auto Clicker are blocked",
        ["Не удалось зарегистрировать глобальную клавишу аварийной остановки. Закройте программу, которая перехватывает Ctrl+Shift+F6 и Ctrl+Shift+F12, затем перезапустите Bench Auto Clicker."] = "The global emergency-stop hotkey could not be registered. Close the app using Ctrl+Shift+F6 and Ctrl+Shift+F12, then restart Bench Auto Clicker.",
        ["Минимальная пауза не может быть больше максимальной."] = "The minimum pause cannot exceed the maximum.",
        ["Сохранённая точка больше не находится на подключённом мониторе. Сохраните её заново клавишей F7."] = "The saved point is no longer on a connected screen. Save it again with F7.",
        ["Центр выбранного экрана находится под окном Bench Auto Clicker. Переместите окно в сторону или сохраните точку клавишей F7."] = "Bench Auto Clicker covers the selected screen center. Move this window aside or save a point with F7.",
        ["Сохранённая точка находится внутри Bench Auto Clicker. Выберите точку в другом окне."] = "The saved point is inside Bench Auto Clicker. Choose a point in another window.",
        ["Выберите вне окна"] = "Choose outside",
        ["Центр"] = "Center",
        ["Точка не задана: центр экрана под курсором. F7 — сохранить точку."] = "No saved point: the center of the screen under the cursor. F7 saves a point.",
        ["Цель: центр экрана под курсором"] = "Target: center of the screen under the cursor",
        ["Остановлено: ошибка ввода"] = "Stopped: input error",
        ["Работа остановлена из-за ошибки"] = "Stopped because of an error",
        ["Сессия завершена"] = "Session complete",
        ["∞ кликов"] = "∞ clicks",
        ["Следующий случайный клик"] = "Next random click",
        ["Пауза между сериями"] = "Pause between bursts",
        ["Bench Auto Clicker не смог зарегистрировать Ctrl+Shift+F6 или резервную Ctrl+Shift+F12. Запуск заблокирован, пока нет надёжной аварийной остановки."] = "Bench Auto Clicker could not register Ctrl+Shift+F6 or the fallback Ctrl+Shift+F12. Starting is blocked until an emergency-stop hotkey is available.",
        ["Горячие клавиши заняты"] = "Hotkeys unavailable",
        ["Аварийная остановка"] = "Emergency stop",
        ["Остановлено перед переходом в сон"] = "Stopped before sleep",
        ["Остановлено при блокировке Windows"] = "Stopped when Windows was locked",
        ["Клики не возобновятся автоматически"] = "Clicking will not resume automatically",
        ["F6 Старт"] = "F6 Start",
        ["F6 локально"] = "F6 in app",
        ["F7 Точка"] = "F7 Point",
        ["F7 локально"] = "F7 in app",
        ["Аварийная клавиша недоступна"] = "Emergency hotkey unavailable",
        ["Некоторые горячие клавиши доступны только в окне"] = "Some hotkeys work only inside the app",
        ["Проверьте настройки"] = "Check settings"
    };

    public static string Language { get; private set; } = "system";
    public static bool IsRussian { get; private set; } = SystemUiCulture.TwoLetterISOLanguageName == "ru";
    public static event Action? LanguageChanged;

    public static string NormalizeLanguage(string? language) => language?.ToLowerInvariant() switch
    {
        "ru" => "ru",
        "en" => "en",
        _ => "system"
    };

    internal static string GetEffectiveLanguage(string? language, CultureInfo? systemUiCulture = null)
    {
        var normalized = NormalizeLanguage(language);
        return normalized == "ru" ||
               (normalized == "system" && (systemUiCulture ?? SystemUiCulture).TwoLetterISOLanguageName == "ru")
            ? "ru"
            : "en";
    }

    public static void SetLanguage(string? language)
    {
        Language = NormalizeLanguage(language);
        IsRussian = GetEffectiveLanguage(Language) == "ru";
        CultureInfo.CurrentUICulture = IsRussian ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = Language == "system" ? SystemNumberCulture : CultureInfo.CurrentUICulture;
        LanguageChanged?.Invoke();
    }

    public static string T(string russian, string english) => IsRussian ? russian : english;

    public static string T(string text)
    {
        if (English.TryGetValue(text, out var english))
        {
            return IsRussian ? text : english;
        }
        if (IsRussian)
        {
            foreach (var pair in English)
            {
                if (pair.Value == text)
                {
                    return pair.Key;
                }
            }
        }
        return text;
    }

    public static string Format(string russian, string english, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(russian, english), values);

    public static void Bind(Control control, string russian, string english)
    {
        TextBindings.Remove(control);
        TextBindings.Add(control, new TextBinding(russian, english));
        control.Text = T(russian, english);
    }

    public static void Bind(Control control, string text)
    {
        if (English.TryGetValue(text, out var translated))
        {
            Bind(control, text, translated);
            return;
        }
        foreach (var pair in English)
        {
            if (pair.Value == text)
            {
                Bind(control, pair.Key, pair.Value);
                return;
            }
        }
        Bind(control, text, text);
    }

    public static void SetToolTip(ToolTip toolTip, Control control, string text)
    {
        var russian = text;
        var english = English.GetValueOrDefault(text, text);
        foreach (var pair in English)
        {
            if (pair.Value == text)
            {
                russian = pair.Key;
                english = pair.Value;
                break;
            }
        }
        TipBindings.Remove(control);
        TipBindings.Add(control, new TipBinding(toolTip, russian, english));
        toolTip.SetToolTip(control, T(russian, english));
    }

    public static void Refresh(Control root)
    {
        if (TextBindings.TryGetValue(root, out var text))
        {
            root.Text = T(text.Russian, text.English);
        }
        if (TipBindings.TryGetValue(root, out var tip))
        {
            tip.ToolTip.SetToolTip(root, T(tip.Russian, tip.English));
        }
        foreach (Control child in root.Controls)
        {
            Refresh(child);
        }
    }

    private sealed record TextBinding(string Russian, string English);
    private sealed record TipBinding(ToolTip ToolTip, string Russian, string English);
}
