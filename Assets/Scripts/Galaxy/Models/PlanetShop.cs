using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    public class PlanetShop
    {
        // goodId → запись с ценой и стоком
        public Dictionary<string, ShopGoodEntry> Goods { get; set; } = new();
    }

    public class ShopGoodEntry
    {
        public int Stock    { get; set; }  // текущий сток (единицы веса)
        public int BuyPrice { get; set; }  // цена покупки у планеты (за 1 ед. веса)
        public int SellPrice{ get; set; }  // цена продажи планете (за 1 ед. веса)
    }

    public class PlanetEquipmentShop
    {
        // uid → экземпляр оборудования в продаже
        public Dictionary<string, ItemInstance> Items { get; set; } = new();
    }
}
