using UnityEngine;
using SRG.Simulation;

namespace SRG.Config
{
    public static class GalaxyConstants
    {
        public const string RACE_NONE_KEY = "None";
        public const string RACE_COMMON_KEY = "Common";
        public const string RACE_UNRESOLVED_KEY = "Unresolved";
        public const string RACE_MIXED_KEY = "Mixed";
        public const string OWNER_NONE_KEY = "None";
        public const string OWNER_UNRESOLVED_KEY = "Unresolved";
        public const string OWNER_MIXED_KEY = "Mixed";
        public const string VAL_UNKNOWN = "Unknown";
        public const int ORBIT_FALLBACK = 99;

        public static float SECTOR_RADIUS = 5.0f;

        public static string DEFAULT_COLOR = "white";
        public static string FALLBACK_STAR_COLOR = "White";
        public static string FALLBACK_SAT_SIZE = "Mid";
        public static string FALLBACK_EMBLEM_PATH = "Empty";
        public static string DEFAULT_GALAXY_KEY = "MilkyWay";

        public static string PATH_PLANET_FALLBACK = "Graphics/SpaceObjects/Planets/Textures/Unic/Solar_Earth";
        public static string PATH_SATELLITE_FALLBACK = "Graphics/SpaceObjects/Planets/Textures/Unic/Solar_Moon";
        public static string PATH_SAT_TEXTURES = "Graphics/SpaceObjects/Planets/Textures/Satellites";
        public static string PATH_COMMON_TEXTURES = "Graphics/SpaceObjects/Planets/Textures/Common";
        public static string PATH_ORBITAL = "Graphics/SpaceObjects/Planets/Orbital";
        public static string PATH_PLANET_MASK = "Graphics/SpaceObjects/Planets/Mask/160";
        public static string PATH_CLOUDS = "Graphics/SpaceObjects/Planets/Textures/Clouds";
        public static int CLOUD_VARIANTS_COUNT = 3;

        public static float MIN_ORBIT_RADIUS = 1000f;
        public static float SYSTEM_PADDING = 400f;
        public static float ORBIT_GAP_MIN = 800f;
        public static float ORBIT_GAP_MAX = 1000f;
        public static float FIRST_ORBIT_MIN = 1200f;
        public static float FIRST_ORBIT_MAX = 2000f;

        public static float PLANET_ORBIT_SPEED_MIN = 50f;
        public static float PLANET_ORBIT_SPEED_MAX = 300f;
        public static int PLANET_DAY_SPEED_MIN = 20;
        public static int PLANET_DAY_SPEED_MAX = 60;
        public static float CLOUD_SPEED_FACTOR_MIN = 0.6f;
        public static float CLOUD_SPEED_FACTOR_MAX = 0.8f;
        public static float ORBITAL_OBJ_CHANCE = 0.3f;
        public static float HIGH_DENSITY_CHANCE = 0.05f;
        public static float NONE_ORBIT_MAX_FRACTION = 0.4f;
        public static int FALLBACK_MAX_PLANETS = 12;

        public static float FALLBACK_PLANET_RADIUS = 120f;

        public static float SAT_ORBIT_BASE_OFFSET = 35f;
        public static float SAT_ORBIT_SPACING = 25f;
        public static float SAT_ORBIT_SPEED_MIN = 40f;
        public static float SAT_ORBIT_SPEED_MAX = 120f;
        public static int SAT_DAY_SPEED_MIN = 10;
        public static int SAT_DAY_SPEED_MAX = 39;
        public static float SAT_CLOUD_SPEED_FACTOR = 0.7f;

        public static int DEFAULT_MAX_ASTEROIDS = 2;
        public static float ASTEROID_GRAVITY_CONST = 500f;
        public static float ASTEROID_DAMPING_CONST = 10000f;
        public static string ASTEROID_SPRITES_PATH = "Graphics/SpaceObjects/Asteroids";

        public static string ASTEROID_MINIMAP_ICON_PATH = "Graphics/UI/Minimap/Icons/Asteroid";
        public static string EXPLOSION_SPRITE_PATH = "Graphics/Effects/Explosions/Asteroids/Default/001";
        public static float EXPLOSION_FPS = 12f;
        public static float EXPLOSION_SCALE = 1.5f;

        public static float DROPPED_ITEM_LIFETIME = 30f;
        public static string MINERAL_SPRITES_PATH = "Graphics/Items/Stackable/Goods/Minerals";
        public static int MINERAL_SPRITE_TIER_COUNT = 5;

        /// <summary>
        /// Минимальный вес содержимого контейнера, при котором при захвате контейнер не
        /// разгружается в инвентарь, а буксируется как груз (требует TowingRig у буксирующего).
        /// Сейчас 10 — отладочное значение; будет повышено.
        /// </summary>
        public static int HEAVY_ITEM_WEIGHT_THRESHOLD = 10;

        public static int BG_STAR_COUNT_FAR = 25;
        public static int BG_STAR_COUNT_MID = 15;
        public static int BG_STAR_COUNT_MID_NEAR = 6;
        public static int BG_STAR_COUNT_NEAR = 8;
        public static int BG_TEX_SIZE_FAR = 1024;
        public static int BG_TEX_SIZE_NEAR = 512;
        public static float BG_PARALLAX_FAR = 0.02f;
        public static float BG_PARALLAX_MID = 0.05f;
        public static float BG_PARALLAX_MID_NEAR = 0.08f;
        public static float BG_PARALLAX_NEAR = 0.12f;
        public static float BG_LAYER_TILE_SIZE = 500f;

        public static float BG_VOID_SCALE_FACTOR = 2.5f;

        // Крупные фоновые звёзды с мерцанием
        public static float BG_PARALLAX_STAR_BIG      = 0.10f;
        public static float BG_STAR_BIG_GRID          = 550f;
        public static string LOG_FALLBACK_STAR_COLOR = "silver";
        public static string LOG_NEUTRAL_OWNER_LABEL = "Neutral";

        // ── Прочие Resources-пути (заполняются из GameSettingsConfig) ─────────────
        public static string PATH_PLANET_MINIMAP_ICON = "Graphics/UI/Minimap/Icons/Planet";
        public static string PATH_SHIP_FORM_RES_ROOT  = "Graphics/UI/ShipForm/";
        public static string PATH_SHIP_FORM_SLOTS_CFG = "Config/ShipFormSlots";
        public static string PATH_STAR_MINIMAP_ICONS  = "Graphics/UI/Minimap/Icons/Stars";
        public static string PATH_HYPERJUMP_BEGIN     = "Graphics/Effects/Hyperjump/begin";
        public static string PATH_HYPERJUMP_MID       = "Graphics/Effects/Hyperjump/mid";
        public static string PATH_HYPERJUMP_END       = "Graphics/Effects/Hyperjump/end";
        public static string PATH_CONTAINERS          = "Graphics/Items/Containers";
        public static string PATH_PLANET_TEXTURES_BASE = "Graphics/SpaceObjects/Planets/Textures";
        public static string SHADER_NEBULA         = "Shaders/NebulaWispy";
        public static string SHADER_STAR_ADDITIVE  = "Shaders/StarAdditive";
        public static string SHADER_STAR_LARGE     = "Shaders/StarLarge";
        // Дублируются в GameSettingsConfig.Wormhole_*Path, но зеркалируются сюда для
        // единообразия доступа и снятия null-цепочек GameWorld.Settings?.
        public static string WORMHOLE_OPENING_PATH = "Graphics/Effects/Wormhole/open";
        public static string WORMHOLE_CYCLE_PATH   = "Graphics/Effects/Wormhole/cycle";
        public static string WORMHOLE_CLOSING_PATH = "Graphics/Effects/Wormhole/close";
        public static string WORMHOLE_ICON_PATH    = "Graphics/UI/Minimap/wormhole_map";

        public static void Initialize(GameSettingsConfig cfg)
        {
            if (cfg == null) return;

            DEFAULT_GALAXY_KEY = cfg.DefaultGalaxyKey;
            SECTOR_RADIUS = cfg.SectorRadius;

            FALLBACK_STAR_COLOR = cfg.FallbackStarColor;
            FALLBACK_SAT_SIZE = cfg.FallbackSatelliteSize;
            FALLBACK_EMBLEM_PATH = cfg.FallbackEmblemPath;

            PATH_PLANET_FALLBACK = cfg.FallbackPlanetTexture;
            PATH_SATELLITE_FALLBACK = cfg.FallbackSatelliteTexture;
            PATH_SAT_TEXTURES = cfg.SatelliteTexturesPath;
            PATH_COMMON_TEXTURES = cfg.CommonPlanetTexturesPath;
            PATH_ORBITAL = cfg.OrbitalObjectsPath;
            PATH_PLANET_MASK = cfg.PlanetMaskPath;
            PATH_CLOUDS = cfg.CloudTexturesPath;
            CLOUD_VARIANTS_COUNT = cfg.CloudTexturesCount;

            MIN_ORBIT_RADIUS = cfg.MinOrbitRadius;
            SYSTEM_PADDING = cfg.SystemPadding;
            ORBIT_GAP_MIN = cfg.AvgPlanetDistanceUnits - cfg.PlanetDistanceSpread;
            ORBIT_GAP_MAX = cfg.AvgPlanetDistanceUnits + cfg.PlanetDistanceSpread;
            FIRST_ORBIT_MIN = cfg.FirstOrbitRadiusMin;
            FIRST_ORBIT_MAX = cfg.FirstOrbitRadiusMax;

            PLANET_ORBIT_SPEED_MIN = cfg.PlanetOrbitSpeedMin;
            PLANET_ORBIT_SPEED_MAX = cfg.PlanetOrbitSpeedMax;
            PLANET_DAY_SPEED_MIN = cfg.PlanetDaySpeedMin;
            PLANET_DAY_SPEED_MAX = cfg.PlanetDaySpeedMax;
            CLOUD_SPEED_FACTOR_MIN = cfg.CloudSpeedFactorMin;
            CLOUD_SPEED_FACTOR_MAX = cfg.CloudSpeedFactorMax;
            ORBITAL_OBJ_CHANCE = cfg.OrbitalObjectChance;
            HIGH_DENSITY_CHANCE = cfg.HighDensityChance;
            NONE_ORBIT_MAX_FRACTION = cfg.NoneOrbitMaxFraction;
            FALLBACK_MAX_PLANETS = cfg.FallbackMaxPlanetsPerStar;

            FALLBACK_PLANET_RADIUS = cfg.FallbackPlanetPixelRadius;

            SAT_ORBIT_BASE_OFFSET = cfg.SatelliteOrbitBaseOffset;
            SAT_ORBIT_SPACING = cfg.SatelliteOrbitSpacing;
            SAT_ORBIT_SPEED_MIN = cfg.SatelliteOrbitSpeedMin;
            SAT_ORBIT_SPEED_MAX = cfg.SatelliteOrbitSpeedMax;
            SAT_DAY_SPEED_MIN = cfg.SatelliteDaySpeedMin;
            SAT_DAY_SPEED_MAX = cfg.SatelliteDaySpeedMax;
            SAT_CLOUD_SPEED_FACTOR = cfg.SatelliteCloudSpeedFactor;

            DEFAULT_MAX_ASTEROIDS = cfg.DefaultMaxAsteroids;
            ASTEROID_SPRITES_PATH = cfg.AsteroidSpritesPath;
            ASTEROID_MINIMAP_ICON_PATH = cfg.AsteroidMinimapIconPath;

            EXPLOSION_SPRITE_PATH = cfg.ExplosionSpritePath ?? string.Empty;
            if (cfg.ExplosionFPS > 0f) EXPLOSION_FPS = cfg.ExplosionFPS;
            if (cfg.ExplosionScale > 0f) EXPLOSION_SCALE = cfg.ExplosionScale;

            if (cfg.DroppedItemLifetime > 0f) DROPPED_ITEM_LIFETIME = cfg.DroppedItemLifetime;
            if (!string.IsNullOrEmpty(cfg.MineralSpritesPath)) MINERAL_SPRITES_PATH = cfg.MineralSpritesPath;
            if (cfg.MineralSpriteTierCount > 0) MINERAL_SPRITE_TIER_COUNT = cfg.MineralSpriteTierCount;

            BG_STAR_COUNT_FAR = cfg.BgStarCountFar;
            BG_STAR_COUNT_MID = cfg.BgStarCountMid;
            BG_STAR_COUNT_MID_NEAR = cfg.BgStarCountMidNear;
            BG_STAR_COUNT_NEAR = cfg.BgStarCountNear;
            BG_TEX_SIZE_FAR = cfg.BgTexSizeFar;
            BG_TEX_SIZE_NEAR = cfg.BgTexSizeNear;
            BG_PARALLAX_FAR = cfg.BgParallaxFar;
            BG_PARALLAX_MID = cfg.BgParallaxMid;
            BG_PARALLAX_MID_NEAR = cfg.BgParallaxMidNear;
            BG_PARALLAX_NEAR = cfg.BgParallaxNear;
            BG_LAYER_TILE_SIZE = cfg.BgLayerTileSize;

            BG_VOID_SCALE_FACTOR = cfg.BgVoidScaleFactor;

            LOG_FALLBACK_STAR_COLOR = cfg.LogFallbackStarColor;
            LOG_NEUTRAL_OWNER_LABEL = cfg.LogNeutralOwnerLabel;

            if (!string.IsNullOrEmpty(cfg.PlanetMinimapIconPath))  PATH_PLANET_MINIMAP_ICON  = cfg.PlanetMinimapIconPath;
            if (!string.IsNullOrEmpty(cfg.ShipFormResourceRoot))   PATH_SHIP_FORM_RES_ROOT   = cfg.ShipFormResourceRoot;
            if (!string.IsNullOrEmpty(cfg.ShipFormSlotsConfigPath))PATH_SHIP_FORM_SLOTS_CFG  = cfg.ShipFormSlotsConfigPath;
            if (!string.IsNullOrEmpty(cfg.StarMinimapIconsBasePath))PATH_STAR_MINIMAP_ICONS  = cfg.StarMinimapIconsBasePath;
            if (!string.IsNullOrEmpty(cfg.HyperjumpBeginPath))     PATH_HYPERJUMP_BEGIN      = cfg.HyperjumpBeginPath;
            if (!string.IsNullOrEmpty(cfg.HyperjumpMidPath))       PATH_HYPERJUMP_MID        = cfg.HyperjumpMidPath;
            if (!string.IsNullOrEmpty(cfg.HyperjumpEndPath))       PATH_HYPERJUMP_END        = cfg.HyperjumpEndPath;
            if (!string.IsNullOrEmpty(cfg.ContainersBasePath))     PATH_CONTAINERS           = cfg.ContainersBasePath;
            if (!string.IsNullOrEmpty(cfg.PlanetTexturesBasePath)) PATH_PLANET_TEXTURES_BASE = cfg.PlanetTexturesBasePath;
            if (!string.IsNullOrEmpty(cfg.NebulaShaderPath))       SHADER_NEBULA             = cfg.NebulaShaderPath;
            if (!string.IsNullOrEmpty(cfg.StarAdditiveShaderPath)) SHADER_STAR_ADDITIVE      = cfg.StarAdditiveShaderPath;
            if (!string.IsNullOrEmpty(cfg.StarLargeShaderPath))    SHADER_STAR_LARGE         = cfg.StarLargeShaderPath;
            if (!string.IsNullOrEmpty(cfg.Wormhole_OpeningPath))   WORMHOLE_OPENING_PATH     = cfg.Wormhole_OpeningPath;
            if (!string.IsNullOrEmpty(cfg.Wormhole_CyclePath))     WORMHOLE_CYCLE_PATH       = cfg.Wormhole_CyclePath;
            if (!string.IsNullOrEmpty(cfg.Wormhole_ClosingPath))   WORMHOLE_CLOSING_PATH     = cfg.Wormhole_ClosingPath;
            if (!string.IsNullOrEmpty(cfg.Wormhole_IconPath))      WORMHOLE_ICON_PATH        = cfg.Wormhole_IconPath;
        }

        public static void InitializeFromGalaxyConfig(GalaxyConfig galaxyCfg)
        {
            if (galaxyCfg?.Explosion == null) return;
            var exp = galaxyCfg.Explosion;
            if (exp.FPS > 0f) EXPLOSION_FPS = exp.FPS;
            if (exp.Scale > 0f) EXPLOSION_SCALE = exp.Scale;
        }
    }
}
