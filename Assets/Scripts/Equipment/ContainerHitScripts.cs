using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Equipment
{
    /// <summary>Встроенные обработчики <see cref="IContainerHitScript"/>. Регистрируются
    /// из <see cref="ContainerHitScripts.RegisterBuiltins"/> — вызывать при старте игры.</summary>
    public static class ContainerHitScripts
    {
        public const string BigExplosion = "BigExplosion";

        public static void RegisterBuiltins()
        {
            CargoHitRegistry.Register(BigExplosion, new BigExplosionScript());
        }

        /// <summary>Кварковая бомба: наносит одинаковый Explosive-урон всем кораблям в радиусе.
        /// Настройка через <c>Params</c> предмета: <c>Radius</c> (=250), <c>MinDamage</c>/<c>MaxDamage</c>
        /// (400..800), <c>ArmorPenetration</c> (0), <c>ShieldPenetration</c> (0..1). Свой контейнер
        /// исключается — он уже мёртв.</summary>
        private sealed class BigExplosionScript : IContainerHitScript
        {
            public void OnContainerHit(ShipData container, ItemInstance item, ShipData killer,
                                       ItemsConfig equipConfig)
            {
                if (container?.CurrentStar == null || item == null) return;
                float radius = SRUnits.ToWorld(item.GetParam("Radius", 250f));
                if (radius <= 0f) return;

                float minDmg = item.GetParam("MinDamage", 400f);
                float maxDmg = item.GetParam("MaxDamage", 800f);
                int armorPen  = Mathf.RoundToInt(item.GetParam("ArmorPenetration", 0f));
                float shieldPen = Mathf.Clamp01(item.GetParam("ShieldPenetration", 0f));

                var star = container.CurrentStar;
                Vector2 center = container.Position;
                float r2 = radius * radius;

                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var victim = star.Ships[i];
                    if (victim == null || victim == container) continue;
                    if (victim.CurrentHull <= 0) continue;
                    if ((victim.Position - center).sqrMagnitude > r2) continue;

                    float baseDmg = Random.Range(minDmg, maxDmg);
                    var shot = new WeaponShotParams
                    {
                        AttackerUid      = killer?.Uid,
                        TargetUid        = victim.Uid,
                        MinDmg           = baseDmg,
                        MaxDmg           = baseDmg,
                        ArmorPenetration = armorPen,
                        ShieldPenetration = shieldPen,
                        DamageType       = DamageType.Explosive,
                        HitPattern       = HitPattern.AoE,
                    };
                    var (hullDmg, _) = WeaponSystem.CalculateDamage(baseDmg, shot, victim, killer);
                    victim.CurrentHull = Mathf.Max(0, victim.CurrentHull - Mathf.RoundToInt(hullDmg));
                    if (victim.CurrentHull <= 0)
                        WeaponSystem.RegisterTargetDeath(
                            victim, PlayerDeathCause.Weapon,
                            killer?.Name ?? item.Name, killer?.Owner ?? "",
                            anim: null, killer);
                }
            }
        }
    }
}
