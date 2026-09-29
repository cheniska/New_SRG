using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private const float StellarD0 = 2800f;

        private void FinalizeStarProps(StarData star)
        {
            if (star.Type == GalaxyConstants.VAL_UNKNOWN)
                star.Type = _ctx.AvailableStarTypes[UnityEngine.Random.Range(0, _ctx.AvailableStarTypes.Count)];
            if (star.Color == GalaxyConstants.VAL_UNKNOWN) GenerationHelpers.ApplyWeightedStarColor(star, _ctx.Config);
            if (star.GraphVar == 0) star.GraphVar = GenerationHelpers.GetRandomColorVariant(star.Color, _ctx.Config);

            star.MapIcon = GenerationHelpers.BuildStarMapIcon(star.Color, star.GraphVar);
            star.BackgroundSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            StarTypeData typeData = null;
            float hzSizeMult = 1f;
            if (_ctx.Config?.Stars?.Types != null &&
                _ctx.Config.Stars.Types.TryGetValue(star.Type, out typeData))
            {
                star.MassSolar = typeData.MassSolar;
                hzSizeMult = typeData.HZ_SizeMult;
                star.StarRadius = 0.5f * typeData.GraphicSizeMult;
                if (!Mathf.Approximately(hzSizeMult, 1f))
                    foreach (var planet in star.Planets)
                        planet.OrbitRadius *= hzSizeMult;
            }
            else
                star.StarRadius = 0.5f;

            star.SystemSize = CalculateSystemSize(star.Planets);

            if (_ctx.Config?.Stars?.Colors != null &&
                _ctx.Config.Stars.Colors.TryGetValue(star.Color, out var colorData))
            {
                star.RadiationMult = colorData.RadiationMult;
                star.HabitableZoneMin = colorData.HabitableZoneMin_u;
                star.HabitableZoneMax = colorData.HabitableZoneMax_u;
                star.HabitableZoneMid = colorData.HabitableZoneMid_u;
                star.FlareRisk = colorData.FlareRisk;
            }

            foreach (var planet in star.Planets)
            {
                RollStellarPhysics(planet, star, hzSizeMult);
                CheckRaceConditions(planet);
            }

            CustomPropertyResolver.ResolveAndApplyProperties(star, star.CustomProperties, "Stars", _ctx.Config);
        }

        private static void RollStellarPhysics(PlanetData planet, StarData star, float hzSizeMult)
        {
            float d = planet.OrbitRadius * hzSizeMult;
            if (d < 0.001f) return;

            float lStar = Mathf.Pow(star.MassSolar, 1.267f) * star.RadiationMult;
            float dRatio = d / StellarD0;

            planet.SolarFlux = lStar / (dRatio * dRatio);
            float baseTemp = 278f * Mathf.Pow(lStar, 0.25f) / Mathf.Sqrt(dRatio);
            float g = planet.SurfaceGravity;
            float gRatio = g / 9.81f;
            if (planet.Density < 2f)
                planet.MagneticField = Mathf.Clamp(Mathf.Pow(gRatio, 1.2f) / 1.4f, 0f, 3f);
            else if (planet.Density < 3f)
                planet.MagneticField = Mathf.Clamp(
                    Mathf.Sqrt(gRatio) * planet.GeoActivity * (1f + 0.2f * planet.SatellitesCount) / 0.77f,
                    0f, 3f);
            else
                planet.MagneticField = Mathf.Clamp(
                    Mathf.Sqrt(gRatio) * Mathf.Pow(planet.GeoActivity, 3f) * (1f + 0.15f * planet.SatellitesCount) / 1.15f,
                    0f, 3f);

            if (planet.Density < 2f)
                planet.AtmPressure = Mathf.Min(10f, Mathf.Pow(gRatio, 1.5f) * 5f);
            else if (planet.Density < 3f)
                planet.AtmPressure = Mathf.Clamp(Mathf.Pow(gRatio, 0.8f) * Mathf.Pow(1f / planet.SolarFlux, 0.3f) * 0.5f, 0f, 3f);
            else
                planet.AtmPressure = gRatio * Mathf.Pow(planet.GeoActivity, 2f) * Mathf.Sqrt(1f + planet.MagneticField) / (Mathf.Sqrt(planet.SolarFlux) * 1.4142f);

            if (planet.AtmPressureFixed.HasValue)
                planet.AtmPressure = planet.AtmPressureFixed.Value;

            planet.SurfaceRadiation = planet.SolarFlux * 1000f * 1.361f / ((1f + 0.36f * planet.AtmPressure) * (1f + 0.36f * planet.MagneticField));

            if (planet.Density < 2f)
            {
                planet.WaterAbundance = 0f;
            }
            else
            {
                float satBonus = Mathf.Clamp(planet.SatellitesCount * 0.08f, 0f, 0.2f);
                planet.WaterAbundance = Mathf.Clamp(
                    (planet.GeoActivity + satBonus) * planet.MagneticField * (1f - planet.SolarFlux * 0.343f),
                    0f, 1f);
            }

            if (planet.WaterAbundanceFixed.HasValue)
                planet.WaterAbundance = planet.WaterAbundanceFixed.Value;

            float greenhouseFactor = Mathf.Log(1f + planet.AtmPressure) * (1f + 0.5f * planet.WaterAbundance);
            planet.SurfaceTemp = baseTemp + 10f * greenhouseFactor;

            if (planet.OxygenPercentFixed.HasValue)
            {
                planet.OxygenPercent = planet.OxygenPercentFixed.Value;
            }
            else
            {
                float oxyBase      = planet.WaterAbundance
                                   * Mathf.Clamp01(planet.MagneticField)
                                   * Mathf.Clamp01(planet.AtmPressure);
                float fluxFactor   = 1f / (1f + 0.6f * Mathf.Max(0f, planet.SolarFlux - 1f));
                float geoFactor    = 1f / (1f + 0.8f * planet.GeoActivity * planet.GeoActivity);
                planet.OxygenPercent = Mathf.Clamp(oxyBase * fluxFactor * geoFactor * 52f, 0f, 35f);
            }

            if (planet.SurfaceTempFixed.HasValue)
                planet.SurfaceTemp = planet.SurfaceTempFixed.Value;

            RecomputeHydrologyState(planet);
            RollSurfaceTypes(planet);
        }

        private static void RollSurfaceTypes(PlanetData planet)
        {
            // Жидкость — зависит от обилия воды и гидрологического состояния
            float rawLiquid = planet.HydrologyState switch
            {
                2 => planet.WaterAbundance * 75f,   // жидкая вода (Земля ~71%)
                1 => planet.WaterAbundance * 8f,    // преимущественно лёд
                3 => planet.WaterAbundance * 15f,   // сверхкритическое/лавовое
                _ => 0f                              // безводная пустошь
            };
            rawLiquid = Mathf.Clamp(rawLiquid, 0f, 80f);

            // Горы — зависят от геологической активности
            float rawMountains = Mathf.Clamp(planet.GeoActivity * 40f + 5f, 5f, 55f);

            float liquid    = planet.SurfaceLiquidFixed    ?? rawLiquid;
            float mountains = planet.SurfaceMountainsFixed ?? rawMountains;

            float total = liquid + mountains;
            if (total > 100f)
            {
                float scale = 100f / total;
                liquid    *= scale;
                mountains *= scale;
            }

            planet.SurfaceLiquid    = Mathf.Round(liquid);
            planet.SurfaceMountains = Mathf.Round(mountains);
            planet.SurfacePlains    = 100f - planet.SurfaceLiquid - planet.SurfaceMountains;
        }

        private static void RecomputeHydrologyState(PlanetData planet)
        {
            if (planet.WaterAbundance < 0.05f)
                planet.HydrologyState = 0;
            else if (planet.SurfaceTemp < 200f)
                planet.HydrologyState = 0;
            else if (planet.SurfaceTemp < 273f)
                planet.HydrologyState = 1;
            else if (planet.SurfaceTemp < 647f && planet.AtmPressure > 0.05f)
                planet.HydrologyState = 2;
            else
                planet.HydrologyState = 3;
        }

        private void CheckRaceConditions(PlanetData planet)
        {
            if (planet.Race == GalaxyConstants.RACE_NONE_KEY) return;
            if (_ctx.Config?.Races == null) return;

            // Сначала пробуем уже назначенную расу. Если не подходит — перебираем остальные планетарные расы,
            // чтобы не оставлять планету пустой только из-за неудачного броска ResolveRandomRace.
            if (TryAssignRaceToPlanet(planet, planet.Race)) return;

            string originalRace = planet.Race;
            if (_ctx.Config.Races != null)
                foreach (var key in _ctx.Config.Races.Keys)
                {
                    if (key == originalRace) continue;
                    if (key == GalaxyConstants.RACE_NONE_KEY) continue;
                    if (IsNonPlanetaryRace(key)) continue;
                    if (TryAssignRaceToPlanet(planet, key)) return;
                }

            planet.Race            = GalaxyConstants.RACE_NONE_KEY;
            planet.Owner           = GalaxyConstants.OWNER_NONE_KEY;
            planet.CurrentColor    = GenerationHelpers.GetRaceColor(planet.Race, _ctx.AvailableRaces);
            planet.OrbitalObjects  = string.Empty;
            planet.IsTerraformable = false;
            planet.IsTerraformed   = false;
        }

        /// <summary>
        /// Пытается присвоить указанную расу планете. Если планета подходит по Acceptable — присваивает и возвращает true.
        /// Если подходит только по Terraformable — присваивает, терраформирует и возвращает true.
        /// Иначе оставляет планету без изменений и возвращает false.
        /// </summary>
        private bool TryAssignRaceToPlanet(PlanetData planet, string raceKey)
        {
            if (!_ctx.Config.Races.TryGetValue(raceKey, out var raceConfig)) return false;
            var cond = raceConfig.PlanetConditions;
            if (cond == null) return false;

            float partialO2 = (planet.OxygenPercent / 100f) * planet.AtmPressure;

            bool habitable = true;
            if (cond.WaterAbundanceMin.HasValue   && planet.WaterAbundance     < cond.WaterAbundanceMin.Value)   habitable = false;
            if (cond.WaterAbundanceMax.HasValue   && planet.WaterAbundance     > cond.WaterAbundanceMax.Value)   habitable = false;
            if (cond.OxygenPartialMin.HasValue    && partialO2                 < cond.OxygenPartialMin.Value)    habitable = false;
            if (cond.OxygenPartialMax.HasValue    && partialO2                 > cond.OxygenPartialMax.Value)    habitable = false;
            if (cond.AtmPressureMin.HasValue      && planet.AtmPressure        < cond.AtmPressureMin.Value)      habitable = false;
            if (cond.AtmPressureMax.HasValue      && planet.AtmPressure        > cond.AtmPressureMax.Value)      habitable = false;
            if (cond.SurfaceRadiationMin.HasValue && planet.SurfaceRadiation   < cond.SurfaceRadiationMin.Value) habitable = false;
            if (cond.SurfaceRadiationMax.HasValue && planet.SurfaceRadiation   > cond.SurfaceRadiationMax.Value) habitable = false;
            if (cond.GMin.HasValue                && planet.SurfaceGravity     < cond.GMin.Value)                habitable = false;
            if (cond.GMax.HasValue                && planet.SurfaceGravity     > cond.GMax.Value)                habitable = false;
            if (cond.SurfaceTempMin.HasValue      && planet.SurfaceTemp        < cond.SurfaceTempMin.Value)      habitable = false;
            if (cond.SurfaceTempMax.HasValue      && planet.SurfaceTemp        > cond.SurfaceTempMax.Value)      habitable = false;

            if (habitable)
            {
                planet.Race            = raceKey;
                planet.CurrentColor    = GenerationHelpers.GetRaceColor(raceKey, _ctx.AvailableRaces);
                planet.IsTerraformable = false;
                planet.IsTerraformed   = false;
                return true;
            }

            bool terraformable = true;
            if (cond.WaterAbundanceTfMin.HasValue   && planet.WaterAbundance   < cond.WaterAbundanceTfMin.Value)   terraformable = false;
            if (cond.WaterAbundanceTfMax.HasValue   && planet.WaterAbundance   > cond.WaterAbundanceTfMax.Value)   terraformable = false;
            if (cond.OxygenPartialTfMin.HasValue    && partialO2               < cond.OxygenPartialTfMin.Value)    terraformable = false;
            if (cond.OxygenPartialTfMax.HasValue    && partialO2               > cond.OxygenPartialTfMax.Value)    terraformable = false;
            if (cond.AtmPressureTfMin.HasValue      && planet.AtmPressure      < cond.AtmPressureTfMin.Value)      terraformable = false;
            if (cond.AtmPressureTfMax.HasValue      && planet.AtmPressure      > cond.AtmPressureTfMax.Value)      terraformable = false;
            if (cond.SurfaceRadiationTfMin.HasValue && planet.SurfaceRadiation < cond.SurfaceRadiationTfMin.Value) terraformable = false;
            if (cond.SurfaceRadiationTfMax.HasValue && planet.SurfaceRadiation > cond.SurfaceRadiationTfMax.Value) terraformable = false;
            if (cond.GTfMin.HasValue                && planet.SurfaceGravity   < cond.GTfMin.Value)                terraformable = false;
            if (cond.GTfMax.HasValue                && planet.SurfaceGravity   > cond.GTfMax.Value)                terraformable = false;
            if (cond.SurfaceTempTfMin.HasValue      && planet.SurfaceTemp      < cond.SurfaceTempTfMin.Value)      terraformable = false;
            if (cond.SurfaceTempTfMax.HasValue      && planet.SurfaceTemp      > cond.SurfaceTempTfMax.Value)      terraformable = false;

            if (terraformable)
            {
                planet.Race            = raceKey;
                planet.CurrentColor    = GenerationHelpers.GetRaceColor(raceKey, _ctx.AvailableRaces);
                planet.IsTerraformable = false;
                ExpansionApplyOptimal(planet, raceConfig);
                planet.IsTerraformed   = true;
                return true;
            }

            return false;
        }
    }
}
