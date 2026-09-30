using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Dialog;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class PlanetUIController
    {
        // ── Overview ───────────────────────────────────────────────

        private void RefreshOverview()
        {
            if (!_bodyTexts.TryGetValue(SCR_OVERVIEW, out var txt)) return;
            var sb = new StringBuilder(512);

            // Станция — не поселение: показываем только имя/командира, без строя/населения/
            // экономики/поверхности. Вкладки магазина/оборудования/ангара работают как у планет.
            if (_site is ShipData station && station.IsStation)
            {
                sb.AppendLine($"<b>{station.Name}</b>");
                sb.AppendLine("Тип: Космическая станция");
                sb.AppendLine($"Раса: {station.Race ?? "—"}   Командир: {station.Owner ?? "—"}");
                sb.AppendLine();
                sb.AppendLine("Доступно: торговля, магазин оборудования, ангар, связь с командиром.");
                txt.text = sb.ToString();
                return;
            }

            if (_planet == null) { txt.text = ""; return; }
            sb.AppendLine($"<b>{_planet.Name}</b>");
            sb.AppendLine($"Тип: {_planet.Type}   Размер: {_planet.Size}");
            if (_planet.IsTerraformed)
                sb.AppendLine("Терраформирована");
            string tempArrow = "";
            if (_planet.IsTerraformed && _planet.TerraformOriginals.TryGetValue("SurfaceTemp", out float origTemp))
                tempArrow = _planet.SurfaceTemp > origTemp + 0.001f ? " ↑" : _planet.SurfaceTemp < origTemp - 0.001f ? " ↓" : "";
            sb.AppendLine($"Температура поверхности: {_planet.SurfaceTemp:F1} К{tempArrow}");
            sb.AppendLine($"Раса: {_planet.Race ?? "—"}   Owner: {_planet.Owner ?? "—"}");
            sb.AppendLine($"Строй: {_planet.Settlement.Government ?? "Неизвестно"}");
            sb.AppendLine();
            if (_planet.Settlement.Population > 0)
                sb.AppendLine($"Население:  {_planet.Settlement.Population:N0}");
            if (!string.IsNullOrEmpty(_planet.Settlement.EconomyType))
                sb.AppendLine($"Экономика:  {_planet.Settlement.EconomyType}");
            if (_planet.Settlement.TechLevel > 0)
            {
                var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
                var ptuCfg = GalaxyManager.Instance?.Context?.Config?.Planets?.PTU;
                float threshold = ptuCfg?.Research?.ProgressPerLevel ?? 100f;
                sb.AppendLine($"ПТУ: {_planet.Settlement.TechLevel}  (прогресс: {_planet.Settlement.PtuProgress:F1}/{threshold:F0})");
                if (galaxy != null)
                    sb.AppendLine($"ГТУ: {galaxy.GtuLevel}");
            }
            if (_planet.Settlement.ActiveEvents != null && _planet.Settlement.ActiveEvents.Count > 0)
            {
                var cfg = GalaxyManager.Instance?.Context?.Config;
                sb.AppendLine();
                sb.AppendLine("── События ──");
                foreach (var evt in _planet.Settlement.ActiveEvents)
                {
                    string evtName = cfg?.Events != null && cfg.Events.TryGetValue(evt.EventId, out var ec)
                        ? ec.DisplayName : evt.EventId;
                    string dur = evt.RemainingMonths >= 0 ? $"ещё {evt.RemainingMonths} мес." : "бессрочно";
                    sb.AppendLine($"  {evtName} ({dur})");
                }
            }
            sb.AppendLine();
            sb.AppendLine("── Поверхность ──");
            sb.AppendLine($"Жидкость:  {_planet.AreaLiquid:F0} ({(int)_planet.SurfaceLiquid}%)");
            sb.AppendLine($"Равнины:   {_planet.AreaPlains:F0} ({(int)_planet.SurfacePlains}%)");
            sb.AppendLine($"Горы:      {_planet.AreaMountains:F0} ({(int)_planet.SurfaceMountains}%)");
            sb.AppendLine($"Итого:     {_planet.TotalSurfaceArea:F0} млн км²");
            sb.AppendLine();
            sb.AppendLine($"Орбита #{_planet.OrbitIndex}   Спутников: {_planet.Satellites.Count}");
            txt.text = sb.ToString();
        }
    }
}
