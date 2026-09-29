using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private void GenerateSatellites(PlanetData parent, Dictionary<string, FixedSatelliteData> fixedSats)
        {
            parent.Satellites = new List<SatelliteData>();
            int idx = 0;

            if (fixedSats != null)
                foreach (var (key, fs) in fixedSats)
                {
                    var sat = CreateSatellite(parent, fs, idx);
                    if (string.IsNullOrEmpty(sat.Name)) sat.Name = key;
                    parent.Satellites.Add(sat);
                    idx++;
                }

            int target = Mathf.Max(parent.Satellites.Count, parent.SatellitesCount);
            for (int i = parent.Satellites.Count; i < target; i++)
                parent.Satellites.Add(CreateSatellite(parent, null, i));
        }

        private SatelliteData CreateSatellite(PlanetData parent, FixedSatelliteData fs, int index)
        {
            var sat = new SatelliteData();
            if (fs != null) ApplyFixedSatelliteProps(sat, fs, parent);
            else ApplyRandomSatelliteProps(sat, parent, index);
            FinalizeSatelliteProps(sat, parent, index);
            return sat;
        }

        private void ApplyFixedSatelliteProps(SatelliteData sat, FixedSatelliteData fs, PlanetData parent)
        {
            sat.Size = ResolveValidSize(fs.Size, _ctx.AvailableSatelliteSizes, GalaxyConstants.FALLBACK_SAT_SIZE);
            sat.Name = fs.Name ?? string.Empty;
            sat.Graphic = GenerationHelpers.CleanResourcePath(fs.Graphic);
            sat.Atmosphere = GenerationHelpers.CleanResourcePath(fs.Atmosphere);
            sat.OrbitRadius = fs.OrbitRadius ?? 0f;
            sat.OrbitSpeed = fs.OrbitSpeed ?? 0f;
            sat.DaySpeed = fs.DaySpeed ?? 0;
            sat.CloudsSpeed = fs.CloudsSpeed ?? 0;
            sat.Owner = !string.IsNullOrEmpty(fs.CurOwner)
                ? OwnerResolver.ResolveOwner(fs.CurOwner, parent.Owner, _ctx.AvailableOwners)
                : parent.Owner;
            sat.Race = parent.Race;
        }

        private void ApplyRandomSatelliteProps(SatelliteData sat, PlanetData parent, int index)
        {
            sat.Size = ResolveValidSize(null, _ctx.AvailableSatelliteSizes, GalaxyConstants.FALLBACK_SAT_SIZE);
            sat.Name = $"{parent.Name} {ToRoman(index + 1)}";
            sat.Owner = parent.Owner;
            sat.Race  = parent.Race;
        }

        private void FinalizeSatelliteProps(SatelliteData sat, PlanetData parent, int index)
        {
            if (string.IsNullOrEmpty(sat.Graphic)) sat.Graphic = GenerationHelpers.GetRandomSatelliteGraphic();

            float baseR = GenerationHelpers.GetPlanetRadiusInPixels(parent.Size, _ctx.PlanetSizeRules) + GalaxyConstants.SAT_ORBIT_BASE_OFFSET;
            if (sat.OrbitRadius <= 0f) sat.OrbitRadius = baseR + index * GalaxyConstants.SAT_ORBIT_SPACING;

            if (Mathf.Abs(sat.OrbitSpeed) < NearZero)
                sat.OrbitSpeed = UnityEngine.Random.Range(GalaxyConstants.SAT_ORBIT_SPEED_MIN, GalaxyConstants.SAT_ORBIT_SPEED_MAX)
                    * (UnityEngine.Random.value > 0.5f ? 1f : -1f);

            if (sat.DaySpeed <= 0) sat.DaySpeed = UnityEngine.Random.Range(GalaxyConstants.SAT_DAY_SPEED_MIN, GalaxyConstants.SAT_DAY_SPEED_MAX + 1);
            if (sat.CloudsSpeed <= 0) sat.CloudsSpeed = Mathf.RoundToInt(sat.DaySpeed * GalaxyConstants.SAT_CLOUD_SPEED_FACTOR);

            sat.CurrentAngle = UnityEngine.Random.Range(0f, 360f);
            sat.CurrentRotationAngle = UnityEngine.Random.Range(0f, 360f);
        }
    }
}
