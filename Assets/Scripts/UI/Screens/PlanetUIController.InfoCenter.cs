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
        // ── Info Center ────────────────────────────────────────────
        //
        // Заголовок сверху + общий NewsFeedView (чипы-фильтры и карточки новостей) —
        // тот же вид, что и в космическом оверлее инфоцентра.

        private void BuildInfoCenterPanel(Transform parent)
        {
            var panel = MakePanel(parent, $"Screen_{SCR_INFO}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[SCR_INFO] = panel;

            var header = MakeText(panel.transform, "InfoHeader",
                "Инфоцентр — сводки Галактического Совета", 12, FontStyle.Bold, ColAccent);
            SetAnchors(header.gameObject, 0.01f, 0.94f, 0.99f, 1f);

            var feedGo = new GameObject("Feed");
            feedGo.transform.SetParent(panel.transform, false);
            var feedRt = feedGo.AddComponent<RectTransform>();
            SetAnchors(feedGo, 0f, 0f, 1f, 0.94f);
            _newsFeed = NewsFeedView.Create(feedRt, _font);
        }

        private void RefreshInfoCenter() => _newsFeed?.Refresh();
    }
}
