using System;
using UnityEngine;

namespace SRG.Dialog
{
    /// <summary>
    /// Единая точка установки штатных модулей диалогов. Раньше каждый модуль держал собственный
    /// статический флаг «уже зарегистрировано», а список вызовов был расписан вручную в
    /// <c>DialogUIController.Awake</c> — новый модуль требовал правки в двух местах, и забытая
    /// строка проявлялась только тем, что тег молча уезжал игроку как <c>{tag}</c>.
    ///
    /// <para>Здесь же чинится <see cref="DialogService.Reset"/>: он чистит реестры действий и тегов,
    /// а личные флаги модулей после этого не давали им зарегистрироваться заново. Теперь флаг
    /// один и живёт рядом с самим списком, поэтому Reset может честно переустановить всё.</para>
    ///
    /// <para>Регистрации идемпотентны по построению — <c>RegisterAction</c>/<c>RegisterTag</c>
    /// перезаписывают запись по имени, так что повторный прогон безопасен.</para>
    /// </summary>
    public static class DialogModules
    {
        /// <summary>Порядок значения не имеет: модули регистрируют непересекающиеся имена.
        /// Новый модуль добавляется одной строкой сюда — и больше нигде.</summary>
        private static readonly Action[] _modules =
        {
            DialogActions.RegisterDefaults,
            DialogPartnerActions.RegisterDefaults,
            DialogTruceActions.RegisterDefaults,
            DialogExtortionActions.RegisterDefaults,
            DialogCargoRobActions.RegisterDefaults,
            DialogIncomingRobActions.RegisterDefaults,
            DialogProtectActions.RegisterDefaults,
            DialogPreserveItemsActions.RegisterDefaults,
            DialogNavActions.RegisterDefaults,
            DialogJointAttackActions.RegisterDefaults,
            DialogShipTradeActions.RegisterDefaults,
            DialogSbImprovementActions.RegisterDefaults,
        };

        private static bool _installed;

        /// <summary>Поставить штатные модули (один раз за сессию). Вызывается из
        /// <c>DialogUIController.Awake</c>.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            DialogService.ModuleReinstaller = RunAll;
            RunAll();
        }

        private static void RunAll()
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                try { _modules[i](); }
                catch (Exception e)
                {
                    // Один упавший модуль не должен уносить остальные: диалоги без его тегов
                    // ещё как-то работают, а без всех — нет.
                    Debug.LogError($"[DialogModules] Модуль '{_modules[i].Method?.DeclaringType?.Name}' " +
                                   $"не зарегистрировался: {e.Message}");
                }
            }
        }
    }
}
