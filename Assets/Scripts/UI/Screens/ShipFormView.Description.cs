using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;
using SRG.UI.Logic;

namespace SRG.UI.Screens
{
    public partial class ShipFormView
    {
        // Форматирование описания — SRG.UI.Logic.ItemDescription (чистая логика, покрыта тестами).
        public static string BuildItemDescription(ItemInstance item) => ItemDescription.Build(item);
    }
}
