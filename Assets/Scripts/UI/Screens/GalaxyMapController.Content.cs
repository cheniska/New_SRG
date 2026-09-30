using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.Common;
using SRG.Presentation.Map;
using SRG.Ships.Movement;

namespace SRG.UI.Screens
{
    public partial class GalaxyMapController
    {
        // ══════════════════════════════════════════════════════════════════════════
        // Контент

        private void RefreshContent()
        {
            _mapContent.sizeDelta = MapSize;
            RefreshGalaxyTitle();
            RebuildVoronoi();
            SpawnSectorLabels();
            SpawnStarIcons();
            SpawnDebugLabels();
            LogCoordinates();
            AdjustScrollbars();
            RefreshJumpButton();
        }

        private void RefreshGalaxyTitle()
        {
            if (_galaxyNameText == null) return;

            string name = !string.IsNullOrEmpty(_galCfg?.Name) ? _galCfg.Name : "—";
            _galaxyNameText.text = name;

            if (_galaxyTooltipText == null) return;

            int sectorCount = _galaxy?.Sectors?.Count ?? 0;
            int starCount   = 0;
            if (_galaxy?.Sectors != null)
                foreach (var s in _galaxy.Sectors) starCount += s.Stars?.Count ?? 0;

            var grid = _galCfg?.GalaxyGridSize;
            string sizeStr = (grid != null && grid.Length >= 2)
                ? $"{grid[0]} × {grid[1]}"
                : "—";

            _galaxyTooltipText.text =
                $"Секторов: {sectorCount}    Звёзд: {starCount}\n" +
                $"Размер: {sizeStr}";
        }
    }
}
