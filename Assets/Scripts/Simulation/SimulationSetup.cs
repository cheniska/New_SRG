using System;
using Newtonsoft.Json;
using UnityEngine;
using SRG.Config;
using SRG.Dialog;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI;

namespace SRG.Simulation
{
    /// <summary>Сырые JSON-конфиги игры. Galaxy/Texts/Premade обязательны, остальные — опциональны.</summary>
    public sealed class ConfigSources
    {
        public string Galaxy;
        public string Texts;
        public string Premade;
        public string Items;
        public string Dialogs;
        public string Partners;
    }

    /// <summary>
    /// Сборка контекста симуляции из конфигов — без сцены и MonoBehaviour.
    /// Используется <c>SRG.Core.GalaxyManager</c> на старте и тестами/headless-прогоном.
    /// </summary>
    public static class SimulationSetup
    {
        /// <summary>
        /// Распарсить конфиги, проинициализировать глобальные таблицы констант и реестры
        /// симуляции. Возвращает null, если обязательные конфиги отсутствуют или невалидны.
        /// </summary>
        public static GalaxyGenerationContext CreateContext(ConfigSources src, GameSettingsConfig settings)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            if (!GalaxyConfigLoader.TryLoadConfigs(src.Galaxy, src.Texts, src.Premade, src.Items,
                    out var cfg, out var txt, out var pre, out var itemsCfg))
            {
                Debug.LogError("[SimulationSetup] Config initialization failed.");
                return null;
            }

            if (src.Dialogs != null)
            {
                try
                {
                    var dlg = JsonConvert.DeserializeObject<DialogsConfig>(src.Dialogs);
                    if (dlg != null)
                    {
                        cfg.Dialogs = dlg;
                        // Фразы (пулы строк, тексты приветствий, дефолты тегов) живут в TextsConfig —
                        // связываем до валидации, иначе она увидит правила приветствий без текстов.
                        DialogTexts.Link(dlg, txt);
                        DialogConfigValidator.Validate(dlg);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SimulationSetup] Failed to parse DialogsConfig: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("[SimulationSetup] DialogsConfig not assigned — диалоги будут пусты.");
            }

            if (src.Partners != null)
            {
                try
                {
                    var partners = JsonConvert.DeserializeObject<PartnersConfig>(src.Partners);
                    if (partners != null) cfg.Partners = partners;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SimulationSetup] Failed to parse PartnersConfig: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("[SimulationSetup] PartnersConfig not assigned — партнёрство будет использовать дефолты.");
            }

            var context = new GalaxyGenerationContext(cfg, txt, pre, itemsCfg);
            GalaxyConstants.Initialize(settings);
            GalaxyConstants.InitializeFromGalaxyConfig(cfg);
            NpcBalance.LoadFromSettings(settings);
            OwnerRaceRelationsManager.Instance?.Initialize(cfg);
            TriggerBus.EnsureBuiltinsRegistered();
            EmbedConfigValidator.Validate(itemsCfg, cfg);

            // Регистрируем встроенные скрипты артефактов (BigExplosion → Кварковая бомба,
            // SpawnBlackHole → Субпортал) и подписываем шину смерти контейнеров. Идемпотентно.
            ContainerHitScripts.RegisterBuiltins();
            CargoHitRegistry.EnsureHooked();

            // Компиляция Lua-скриптов артефактов (TurnScript/UseScript из ItemsConfig).
            // Должно идти ДО первого хода: скрипты нужны как в симуляции, так и в UI-активациях.
            LuaArtefactScripts.CompileAndRegisterAll(itemsCfg);

            return context;
        }
    }
}
