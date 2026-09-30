using UnityEngine;
using System.Collections.Generic;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Actions;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI
{
    public class DirectiveManager : MonoBehaviour
    {
        public static DirectiveManager Instance { get; private set; }

        private readonly Dictionary<string, List<Directive>> _ownerDirectives = new();

        // Вторичный индекс (ownerId, starUid) → DirectiveSuppressPiracy для O(1) HasAutoSuppress-проверки
        // из AutoIssueReactiveDirectives. Обновляется в Issue/Cancel/SyncPersistence/EnsureIndexedFor.
        private readonly Dictionary<(string ownerId, string starUid), DirectiveSuppressPiracy> _suppressIndex = new();

        // Ссылка на галактику, для которой построен индекс. При смене (загрузка сейва / новая
        // галактика) индекс перестраивается из galaxy.Directives.
        private GalaxyData _indexedGalaxy;

        // Кэш активной партнёрской директивы на корабль. Ключ = UID follower.
        // Экземпляр переиспользуется между тиками, чтобы NpcBrain видел стабильное DebugName
        // и не пересоздавал под-действие каждый ход.
        private readonly Dictionary<string, ActionPartnerAttend> _partnerActions = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Автосоздание HighCommandRegistry — избавляет от необходимости класть его в сцену вручную.
            if (HighCommandRegistry.Instance == null)
            {
                var go = new GameObject("HighCommandRegistry");
                go.AddComponent<HighCommandRegistry>();
                DontDestroyOnLoad(go);
            }
        }

        private void OnEnable()  { GameWorld.OnTurnCalculate += OnTurnCalculate; }
        private void OnDisable() { GameWorld.OnTurnCalculate -= OnTurnCalculate; }

        public void Issue(OwnerDirective directive)
        {
            if (!_ownerDirectives.TryGetValue(directive.OwnerId, out var list))
                _ownerDirectives[directive.OwnerId] = list = new List<Directive>();
            list.Add(directive);
            if (directive is DirectiveSuppressPiracy sp)
                _suppressIndex[(sp.OwnerId, sp.TargetStarUid)] = sp;
            AddToGalaxyPersistence(directive);
            Debug.Log($"[DirectiveManager] Owner directive: {directive.DebugName} for {directive.OwnerId}");
            PostDirectiveNews(directive);
        }

        /// <summary>Список активных owner-директив стороны (для чтения ГШ и UI).</summary>
        public IReadOnlyList<Directive> GetOwnerDirectives(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return System.Array.Empty<Directive>();
            return _ownerDirectives.TryGetValue(ownerId, out var list)
                ? (IReadOnlyList<Directive>)list
                : System.Array.Empty<Directive>();
        }

        private void AddToGalaxyPersistence(Directive d)
        {
            // Во время GalaxyNextDay неактивной галактики (иerarchical events →
            // OccupationAutoRule.Tick / HighCommand.Tick могут выдавать директивы) CurrentTickingGalaxy
            // указывает на неё — persistence не должна попасть в активную. Вне тика fallback на активную.
            var galaxy = GameWorld.TargetGalaxy;
            if (galaxy == null) return;
            galaxy.Directives ??= new List<Directive>();
            if (!galaxy.Directives.Contains(d)) galaxy.Directives.Add(d);
        }

        public void Cancel(string directiveUid)
        {
            foreach (var list in _ownerDirectives.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Uid == directiveUid)
                    {
                        if (list[i] is DirectiveSuppressPiracy sp)
                            _suppressIndex.Remove((sp.OwnerId, sp.TargetStarUid));
                        list.RemoveAt(i);
                        return;
                    }
        }

        public NpcAction GetDirectiveActionFor(ShipData ship, StarData star)
        {
            // Партнёрская директива — приоритетнее фракционных: если корабль подписал контракт,
            // он должен следовать за лидером даже когда его Owner получил AttackSystem/Defend.
            if (ship != null && !string.IsNullOrEmpty(ship.PartnerLeaderUid))
            {
                if (!_partnerActions.TryGetValue(ship.Uid, out var attend) || attend.LeaderUid != ship.PartnerLeaderUid)
                {
                    attend = new ActionPartnerAttend(ship.PartnerLeaderUid);
                    _partnerActions[ship.Uid] = attend;
                }
                return attend;
            }
            // Если ранее был закэширован — освободить (лидер разорвал контракт / follower ушёл).
            if (ship != null && _partnerActions.ContainsKey(ship.Uid))
                _partnerActions.Remove(ship.Uid);

            if (_ownerDirectives.TryGetValue(ship.Owner ?? string.Empty, out var ownerList))
            {
                foreach (var d in ownerList)
                {
                    if (d.IsExpired) continue;
                    if (!d.Matches(ship)) continue;   // фильтр по Race + ShipType-классу
                    if (!d.ShouldObey(ship)) continue;
                    var action = d.GetActionFor(ship, star);
                    if (action != null) return action;
                }
            }

            return null;
        }

        /// <summary>Восстанавливает индекс из galaxy.Directives. Идемпотентно — вызывается при смене
        /// GeneratedGalaxy (новая генерация или загрузка сейва).</summary>
        private void EnsureIndexedFor(GalaxyData galaxy)
        {
            if (_indexedGalaxy == galaxy) return;
            _ownerDirectives.Clear();
            _partnerActions.Clear();
            _suppressIndex.Clear();
            if (galaxy.Directives != null)
            {
                foreach (var d in galaxy.Directives)
                {
                    if (d == null || d.IsExpired) continue;
                    if (d is OwnerDirective od)
                    {
                        if (!_ownerDirectives.TryGetValue(od.OwnerId ?? "", out var list))
                            _ownerDirectives[od.OwnerId ?? ""] = list = new List<Directive>();
                        list.Add(od);
                        if (od is DirectiveSuppressPiracy sp)
                            _suppressIndex[(sp.OwnerId, sp.TargetStarUid)] = sp;
                    }
                }
            }
            _indexedGalaxy = galaxy;
        }

        public bool HasDirectives(string ownerId) =>
            _ownerDirectives.TryGetValue(ownerId, out var list) && list.Count > 0;

        // Каждые сколько ходов выметать из _partnerActions записи умерших/расторгнувших контракт
        // followers (обычная очистка в GetDirectiveActionFor их не видит — мёртвых не тикают).
        private const int PartnerCacheSweepInterval = 10;

        private void OnTurnCalculate(TurnAnimationData _)
        {
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy == null) return;
            EnsureIndexedFor(galaxy);

            TickList(_ownerDirectives, galaxy);

            AutoIssueReactiveDirectives(galaxy);

            if (galaxy.CurrentTurn % PartnerCacheSweepInterval == 0)
                SweepPartnerActions(galaxy);

            // Генштабы фракций: выдают Attack/Defend по стратегии, тикают со своим счётчиком.
            HighCommandRegistry.Instance?.Tick(galaxy);

            // Оккупация по «пустой системе»: за компанию с директивами один раз в ход после
            // того, как корабли отработали свои ходы. Без задержки — срабатывает мгновенно.
            OccupationAutoRule.Tick(galaxy);

            SyncPersistence(galaxy);
        }

        private void SyncPersistence(GalaxyData galaxy)
        {
            galaxy.Directives ??= new List<Directive>();
            galaxy.Directives.Clear();
            foreach (var list in _ownerDirectives.Values)
                for (int i = 0; i < list.Count; i++)
                    if (!list[i].IsExpired) galaxy.Directives.Add(list[i]);
        }

        /// <summary>Удаляет из кэша партнёрских действий записи кораблей, которых больше нет в живых
        /// или у которых контракт разорван — иначе словарь растёт до смены галактики.
        /// Один проход по всей галактике, чтобы собрать снапшот живых followers (дешевле, чем K×N
        /// вызовов FindShipInGalaxy). Работает раз в PartnerCacheSweepInterval ходов.</summary>
        private void SweepPartnerActions(GalaxyData galaxy)
        {
            if (_partnerActions.Count == 0) return;
            var aliveFollowers = new HashSet<string>();
            foreach (var star in galaxy.StarsMap.Values)
                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var s = star.Ships[i];
                    if (s.CurrentHull > 0 && !string.IsNullOrEmpty(s.PartnerLeaderUid))
                        aliveFollowers.Add(s.Uid);
                }

            List<string> stale = null;
            foreach (var key in _partnerActions.Keys)
                if (!aliveFollowers.Contains(key))
                    (stale ??= new List<string>()).Add(key);
            if (stale != null)
                foreach (var key in stale) _partnerActions.Remove(key);
        }

        // Пороги авто-директив подобраны под пошаговую симуляцию. Если придётся часто
        // править — вынести в GameSettingsConfig.
        private const float AutoSuppressCrimeSum = 50f;       // суммарный CrimeRating в звезде
        private const float AutoSuppressPirateRatio = 0.8f;   // пиратская сила vs военная

        /// <summary>
        /// Реактивно выдаёт DirectiveSuppressPiracy военным владельцам, если в звезде
        /// либо много криминала, либо пираты сильнее местного гарнизона.
        /// Все агрегаты (CrimeSum/PiratePower/MilitaryPower/FirstMilitaryOwner) берутся из
        /// power-кэша звезды — без второго прохода по кораблям с CalculateStrength.
        /// </summary>
        private void AutoIssueReactiveDirectives(GalaxyData galaxy)
        {
            foreach (var star in galaxy.StarsMap.Values)
            {
                star.RebuildPowerCache(galaxy.CurrentTurn);

                string militaryOwner = star.FirstMilitaryOwner;
                if (militaryOwner == null) continue;
                bool threatByCrime  = star.CrimeSum >= AutoSuppressCrimeSum;
                bool threatByPower  = star.PiratePower > 0f && star.PiratePower >= star.MilitaryPower * AutoSuppressPirateRatio;
                if (!threatByCrime && !threatByPower) continue;

                if (HasAutoSuppress(militaryOwner, star.Uid)) continue;

                var directive = new DirectiveSuppressPiracy(militaryOwner, star.Uid) { AutoIssued = true };
                Issue(directive);
                Debug.Log($"[DirectiveManager] AUTO: SuppressPiracy для {militaryOwner} в {SpriteUtility.ShortId(star.Uid)} " +
                          $"(crime={star.CrimeSum:F0}, pirate={star.PiratePower:F0}, military={star.MilitaryPower:F0})");
            }
        }

        private bool HasAutoSuppress(string ownerId, string starUid)
        {
            // O(1) через вторичный индекс _suppressIndex (обновляется в Issue/Cancel/EnsureIndexedFor).
            if (!_suppressIndex.TryGetValue((ownerId, starUid), out var sp)) return false;
            if (sp.IsExpired)
            {
                _suppressIndex.Remove((ownerId, starUid));
                return false;
            }
            return true;
        }

        // Аналог секций GalaxyNews.Star.Kling.Attack / Group.WarriorLiberator из документа.
        // Публикуется один раз при выдаче директивы (в т.ч. авто-суперрессия пиратов).
        private static void PostDirectiveNews(OwnerDirective d)
        {
            var galaxy = GameWorld.TargetGalaxy;
            if (galaxy == null) return;

            switch (d)
            {
                case DirectiveAttackSystem atk:
                {
                    galaxy.StarsMap.TryGetValue(atk.TargetStarUid, out var tstar);
                    string tgt = tstar != null ? tstar.Name : "?";
                    string stg = !string.IsNullOrEmpty(atk.StagingStarUid) && galaxy.StarsMap.TryGetValue(atk.StagingStarUid, out var sstar)
                        ? sstar.Name : null;
                    string tplKey = stg != null ? "directive.attack_with_staging" : "directive.attack";
                    string msg = NewsTexts.Format(tplKey,
                        ("owner", d.OwnerId), ("target", tgt), ("staging", stg));
                    GalaxyNewsService.PostForSide(GalaxyNewsService.CAT_ATTACK, msg,
                        tstar?.CurrentSystemController, d.OwnerId);
                    break;
                }
                case DirectiveSuppressPiracy sp:
                {
                    galaxy.StarsMap.TryGetValue(sp.TargetStarUid, out var st);
                    string name = st != null ? st.Name : "?";
                    GalaxyNewsService.PostForSide(GalaxyNewsService.CAT_SYSTEM,
                        NewsTexts.Format("directive.suppress",
                            ("starName", name), ("owner", d.OwnerId)),
                        st?.CurrentSystemController, d.OwnerId);
                    break;
                }
            }
        }

        private void TickList(Dictionary<string, List<Directive>> dict, GalaxyData galaxy)
        {
            foreach (var list in dict.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    list[i].Tick(galaxy);
                    if (list[i].IsExpired)
                    {
                        Debug.Log($"[DirectiveManager] Expired: {list[i].DebugName}");
                        if (list[i] is DirectiveSuppressPiracy sp)
                            _suppressIndex.Remove((sp.OwnerId, sp.TargetStarUid));
                        list.RemoveAt(i);
                    }
                }
        }
    }
}
