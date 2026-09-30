using System;
using UnityEngine;
using SRG.Galaxy;

namespace SRG.Presentation.Common
{
    /// <summary>Аватар игрока в сцене (реализует <c>SRG.Controllers.PlayerShip</c>).</summary>
    public interface IPlayerAvatar
    {
        Transform Transform { get; }
        ShipData ShipData { get; }
        bool IsWeaponModeActive { get; }
    }

    /// <summary>Запросы к визуалу текущей системы (реализует <c>SRG.Core.SystemViewManager</c>).</summary>
    public interface ISystemViewQuery
    {
        bool TryGetAsteroidVisualPosition(string uid, out Vector2 position);
    }

    /// <summary>
    /// То немногое, что презентации нужно от слоя приложения: аватар игрока, визуал системы и
    /// флаг «ввод занят текстовым полем». Заполняется верхним слоем (Controllers/Core); сама
    /// презентация о них не знает, поэтому сборка SRG.Presentation не зависит от SRG.Game.
    /// </summary>
    public static class PresentationContext
    {
        private static IPlayerAvatar _player;
        private static ISystemViewQuery _systemView;

        /// <summary>Аватар игрока или null. Уничтоженный Unity-объект считается отсутствующим.</summary>
        public static IPlayerAvatar Player
        {
            get => IsAlive(_player) ? _player : null;
            set => _player = value;
        }

        public static ISystemViewQuery SystemView
        {
            get => IsAlive(_systemView) ? _systemView : null;
            set => _systemView = value;
        }

        /// <summary>true, если ввод перехвачен текстовым полем (консоль и т.п.) — горячие клавиши
        /// камеры и оверлеев в этот момент игнорируются.</summary>
        public static Func<bool> IsTextInputActive { get; set; } = () => false;

        private static bool IsAlive(object o) => o != null && !(o is UnityEngine.Object uo && uo == null);
    }
}
