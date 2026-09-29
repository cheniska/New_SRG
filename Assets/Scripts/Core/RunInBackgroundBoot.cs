using UnityEngine;
using SRG.Config;
using SRG.UI.Screens;

namespace SRG.Core
{
    /// <summary>
    /// Читает опцию «Run in Background» из PlayerPrefs и применяет её через
    /// <see cref="Application.runInBackground"/> ДО загрузки сцен. Работает в билде и в
    /// Play Mode редактора — при потере фокуса игра продолжает симуляцию.
    ///
    /// PlayerPrefs-ключ: "RunInBackground" (0/1). Значение по умолчанию — 1
    /// (соответствует дефолту <see cref="GameSettingsConfig.RunInBackground"/>).
    ///
    /// Если пользователь меняет опцию в настройках, <c>MenuController.PersistSettings</c>
    /// сам ставит <c>Application.runInBackground</c> заново — этот класс отвечает только
    /// за первичное применение при холодном старте.
    ///
    /// Замечание про редактор: при активной Play Mode Unity продолжает крутить сцену, даже
    /// когда фокус ушёл на другое окно. Единственный нюанс — если сфокусировать другое окно
    /// самого редактора (Scene, Inspector), Game view может подтормаживать (это отдельный
    /// throttling, не связанный с runInBackground).
    /// </summary>
    internal static class RunInBackgroundBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            int stored = PlayerPrefs.GetInt("RunInBackground", 1);
            Application.runInBackground = stored == 1;
        }
    }
}
