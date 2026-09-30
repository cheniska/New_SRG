using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SRG.Config
{
    public static class GalaxyConfigLoader
    {
        public static bool TryLoadConfigs(
            TextAsset gJson, TextAsset tJson, TextAsset pJson, TextAsset iJson,
            out GalaxyConfig config,
            out TextConfig textConfig,
            out PremadeConfig premadeConfig,
            out ItemsConfig itemsConfig)
            => TryLoadConfigs(
                gJson ? gJson.text : null, tJson ? tJson.text : null,
                pJson ? pJson.text : null, iJson ? iJson.text : null,
                out config, out textConfig, out premadeConfig, out itemsConfig);

        /// <summary>То же по сырому JSON — без TextAsset (тесты, headless-прогон).</summary>
        public static bool TryLoadConfigs(
            string gJson, string tJson, string pJson, string iJson,
            out GalaxyConfig config,
            out TextConfig textConfig,
            out PremadeConfig premadeConfig,
            out ItemsConfig itemsConfig)
        {
            config = null;
            textConfig = null;
            premadeConfig = null;
            itemsConfig = null;

            if (gJson == null || tJson == null || pJson == null)
            {
                Debug.LogError("[GalaxyConfigLoader] One or more config assets are missing.");
                return false;
            }

            try
            {
                config = JsonConvert.DeserializeObject<GalaxyConfig>(gJson);
                textConfig = JsonConvert.DeserializeObject<TextConfig>(tJson);
                premadeConfig = JsonConvert.DeserializeObject<PremadeConfig>(pJson);

                if (iJson != null)
                    itemsConfig = JsonConvert.DeserializeObject<ItemsConfig>(iJson);
                else
                    Debug.LogWarning("[GalaxyConfigLoader] ItemsConfig asset is not assigned. Equipment and items will be empty.");

                return ValidateConfig(config);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GalaxyConfigLoader] Parse error: {e.Message}");
                return false;
            }
        }

        private static bool ValidateConfig(GalaxyConfig config)
        {
            if (config?.Galaxies == null || config.Galaxies.Count == 0 || config.Races == null)
            {
                Debug.LogError("[GalaxyConfigLoader] Config is missing Galaxy or Races section.");
                return false;
            }

            bool hasNoneRace = config.Races.Keys.Any(k =>
                k.Equals(GalaxyConstants.RACE_NONE_KEY, StringComparison.OrdinalIgnoreCase));

            if (!hasNoneRace)
                Debug.LogError($"[GalaxyConfigLoader] Config must contain a race with key '{GalaxyConstants.RACE_NONE_KEY}'.");

            return hasNoneRace;
        }
    }
}
