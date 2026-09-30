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
    public class SectorData
    {
        public string Uid { get; set; } = GameRng.NewUid();
        public string Name { get; set; }
        public Vector2 Center { get; set; }
        public List<StarData> Stars { get; set; } = new();
        public int? FixedStarsCount { get; set; }
        public string Owner { get; set; }
        public string Race { get; set; }

        /// <summary>Доминирующий владелец сектора. Вычисляется при генерации, не сохраняется в JSON.</summary>
        [JsonIgnore] public string ResolvedOwnerId { get; set; }
        /// <summary>Подсказка размещения из premade-конфига ("border"/"center"/"corner"/"neighbour"/"random"). Не сохраняется.</summary>
        [JsonIgnore] public string PlaceHint { get; set; }
        /// <summary>UID целевого сектора-соседа (только для PlaceHint="neighbour"). Не сохраняется.</summary>
        [JsonIgnore] public string NeighbourSectorUid { get; set; }

        /// <summary>
        /// Вершины многоугольника ячейки Вороного в координатах сетки галактики.
        /// Вычисляется при генерации; порядок вершин — CCW.
        /// </summary>
        public List<Vector2> VoronoiPolygon { get; set; } = new();

        /// <summary>
        /// Для каждого ребра i (от VoronoiPolygon[i] до VoronoiPolygon[(i+1)%n]):
        /// UID соседнего сектора, или null если ребро — внешняя граница галактики.
        /// </summary>
        public List<string> VoronoiEdgeNeighbors { get; set; } = new();

        /// <summary>Площадь ячейки Вороного. Вычисляется при BuildVoronoi, не сохраняется.</summary>
        [JsonIgnore] public float VoronoiArea { get; set; }

        [JsonConstructor] public SectorData() { }
        public SectorData(string name, Vector2 center) { Name = name; Center = center; }
    }
}
