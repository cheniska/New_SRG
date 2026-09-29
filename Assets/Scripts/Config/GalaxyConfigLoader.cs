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
        {
            config = null;
            textConfig = null;
            premadeConfig = null;
            itemsConfig = null;

            if (!gJson || !tJson || !pJson)
            {
                Debug.LogError("[GalaxyConfigLoader] One or more config assets are missing.");
                return false;
            }

            try
            {
                config = JsonConvert.DeserializeObject<GalaxyConfig>(gJson.text);
                textConfig = JsonConvert.DeserializeObject<TextConfig>(tJson.text);
                premadeConfig = JsonConvert.DeserializeObject<PremadeConfig>(pJson.text);

                if (iJson != null)
                    itemsConfig = JsonConvert.DeserializeObject<ItemsConfig>(iJson.text);
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
