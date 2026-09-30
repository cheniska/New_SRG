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

            if (_ctx.Config?.Stars?.Colors != null &&
                _ctx.Config.Stars.Colors.TryGetValue(star.Color, out var colorData))
            {
                star.RadiationMult = colorData.RadiationMult;
                star.HabitableZoneMin = colorData.HabitableZoneMin_u;
                star.HabitableZoneMax = colorData.HabitableZoneMax_u;
                star.HabitableZoneMid = colorData.HabitableZoneMid_u;
                star.FlareRisk = colorData.FlareRisk;
            }

            if (!star.IsPremade) PlaceHabitableSlot(star, hzSizeMult);
            star.SystemSize = CalculateSystemSize(star.Planets);

            foreach (var planet in star.Planets)
            {
                RollStellarPhysics(planet, star);
                CheckRaceConditions(planet);
            }

            CustomPropertyResolver.ResolveAndApplyProperties(star, star.CustomProperties, "Stars", _ctx.Config);
        }

        /// <summary>
        /// «Слот обитаемой зоны»: с шансом Planets.HabitableSlotChance ближайшая к обитаемой зоне каменная
        /// планета заселяемого размера сдвигается на орбиту с целевой температурой из Planets.HabitableSlotTemp —
        /// только в пределах зазоров с соседними орбитами, порядок планет не меняется.
        /// </summary>
        private void PlaceHabitableSlot(StarData star, float hzSizeMult)
        {
            var pc = _ctx.Config?.Planets;
            if (pc == null || pc.HabitableSlotChance <= 0f || star.Planets == null || star.Planets.Count == 0) return;
            if (UnityEngine.Random.value >= pc.HabitableSlotChance) return;

            float lStar = Mathf.Pow(star.MassSolar, 1.267f) * star.RadiationMult;
            if (lStar <= 0f) return;
            float tMin = pc.HabitableSlotTemp?.Length > 0 ? pc.HabitableSlotTemp[0] : 250f;
            float tMax = pc.HabitableSlotTemp?.Length > 1 ? pc.HabitableSlotTemp[1] : 315f;
            // Целевая равновесная температура: минус типичный парниковый вклад (~8 K при ~1 атм).
            float tEq = Mathf.Max(50f, UnityEngine.Random.Range(tMin, tMax) - 8f);
            float target = StellarD0 * Mathf.Pow(278f * Mathf.Pow(lStar, 0.25f) / tEq, 2f);

            var list = new List<PlanetData>(star.Planets);
            list.Sort((a, b) => a.OrbitRadius.CompareTo(b.OrbitRadius));

            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.HasFixedOrbitRadius || p.Density < 2f || !IsPopulatedSize(p)) continue;
                float dist = Mathf.Abs(p.OrbitRadius - target);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            if (best < 0) return;

            var planet = list[best];
            float e = Mathf.Clamp(planet.OrbitEccentricity, 0f, 0.99f);
            float margin = GalaxyConstants.ORBIT_GAP_MIN * 0.35f * hzSizeMult;
            float lo = best > 0
                ? (list[best - 1].OrbitRadius * (1f + Mathf.Clamp(list[best - 1].OrbitEccentricity, 0f, 0.99f)) + margin) / Mathf.Max(1f - e, 0.01f)
                : GalaxyConstants.FIRST_ORBIT_MIN * 0.5f * hzSizeMult;
            float hi = best + 1 < list.Count
                ? (list[best + 1].OrbitRadius * (1f - Mathf.Clamp(list[best + 1].OrbitEccentricity, 0f, 0.99f)) - margin) / (1f + e)
                : float.MaxValue;
            if (lo > hi) return;
            planet.OrbitRadius = Mathf.Clamp(target, lo, hi);
        }

        private static void RollStellarPhysics(PlanetData planet, StarData star)
        {
            // OrbitRadius уже домножен на HZ_SizeMult выше (FinalizeStarProps). Повторное умножение
            // смещало физику: у Giant планеты «отодвигались» ×12.5 (вечный лёд), у Dwarf — ×0.28 (пекло).
            float d = planet.OrbitRadius;
            if (d < 0.001f) return;

            float lStar = Mathf.Pow(star.MassSolar, 1.267f) * star.RadiationMult;
            float dRatio = d / StellarD0;

            planet.SolarFlux = lStar / (dRatio * dRatio);
            float baseTemp = 278f * Mathf.Pow(lStar, 0.25f) / Mathf.Sqrt(dRatio);
            float g = planet.SurfaceGravity;
            float gRatio = g / 9.81f;
            float geo = planet.GeoActivity;

            // Магнитное поле: у каменных планет — динамо от геоактивности (geo^1.5; прежний geo³ давал почти
            // нулевые поля и тянул за собой воду и кислород).
            if (planet.Density < 2f)
                planet.MagneticField = Mathf.Clamp(Mathf.Pow(gRatio, 1.2f) / 1.4f, 0f, 3f);
            else if (planet.Density < 3f)
                planet.MagneticField = Mathf.Clamp(
                    Mathf.Sqrt(gRatio) * geo * (1f + 0.2f * planet.SatellitesCount) / 0.77f,
                    0f, 3f);
            else
                planet.MagneticField = Mathf.Clamp(
                    Mathf.Sqrt(gRatio) * Mathf.Pow(geo, 1.5f) * (1f + 0.15f * planet.SatellitesCount) / 0.5f,
                    0f, 3f);
            float fieldShield = Mathf.Clamp01(planet.MagneticField);

            // Давление: дегазация (geo) × удержание (гравитация, поле) / звёздный ветер, логнормальный разброс.
            if (planet.Density < 2f)
                planet.AtmPressure = Mathf.Min(10f, Mathf.Pow(gRatio, 1.5f) * 5f);
            else
            {
                float outgas   = 0.3f + 1.4f * geo;
                float retained = 0.6f + 0.4f * fieldShield;
                float pressure = Mathf.Pow(gRatio, 1.3f) * outgas * retained / Mathf.Sqrt(Mathf.Max(planet.SolarFlux, 0.05f));
                planet.AtmPressure = Mathf.Min(90f, pressure * Mathf.Pow(10f, UnityEngine.Random.Range(-0.45f, 0.45f)));
            }

            if (planet.AtmPressureFixed.HasValue)
                planet.AtmPressure = planet.AtmPressureFixed.Value;

            planet.SurfaceRadiation = planet.SolarFlux * 1000f * 1.361f / ((1f + 0.36f * planet.AtmPressure) * (1f + 0.36f * planet.MagneticField));

            // Вода: запас летучих при формировании × удержание (гравитация, поле) × потери от нагрева.
            if (planet.Density < 2f)
                planet.WaterAbundance = 0f;
            else
            {
                float volatiles = Mathf.Pow(UnityEngine.Random.value, 0.8f);
                float retention = Mathf.Clamp01(gRatio / 0.5f) * (0.55f + 0.45f * fieldShield);
                float heatLoss  = 1f + Mathf.Max(0f, planet.SolarFlux - 1.25f) * 1.2f;
                planet.WaterAbundance = Mathf.Clamp01(volatiles * retention / heatLoss);
            }

            if (planet.WaterAbundanceFixed.HasValue)
                planet.WaterAbundance = planet.WaterAbundanceFixed.Value;

            float greenhouseFactor = Mathf.Log(1f + planet.AtmPressure) * (1f + 0.5f * planet.WaterAbundance);
            planet.SurfaceTemp = baseTemp + 10f * greenhouseFactor;

            // Кислород: свободный O₂ — признак фотосинтезирующей биосферы, которой нужна жидкая вода.
            // Без биосферы — абиогенные следы (фотолиз).
            if (planet.OxygenPercentFixed.HasValue)
                planet.OxygenPercent = planet.OxygenPercentFixed.Value;
            else
            {
                bool liquidWater = planet.Density >= 2f && planet.WaterAbundance >= 0.08f
                                   && planet.SurfaceTemp >= 255f && planet.SurfaceTemp <= 335f
                                   && planet.AtmPressure >= 0.2f;
                bool biosphere = liquidWater && UnityEngine.Random.value < Mathf.Min(0.85f, 0.35f + planet.WaterAbundance);
                planet.OxygenPercent = biosphere ? UnityEngine.Random.Range(12f, 30f) : UnityEngine.Random.Range(0f, 1.5f);
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

            switch (RaceHabitability.Evaluate(planet, cond))
            {
                case HabitabilityLevel.Habitable:
                    planet.Race            = raceKey;
                    planet.CurrentColor    = GenerationHelpers.GetRaceColor(raceKey, _ctx.AvailableRaces);
                    planet.IsTerraformable = false;
                    planet.IsTerraformed   = false;
                    return true;
                case HabitabilityLevel.Terraformable:
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
