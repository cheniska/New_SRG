using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.NpcAI.Spawning;
using SRG.Ships;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.Scripting;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    public partial class GameConsoleController
    {
        void Log(string m)
        {
            // GameLog.Add зовётся и из расчёта хода (фоновый поток).
            if (!MainThread.IsCurrent) { MainThread.Post(() => Log(m)); return; }
            _logs.Add(m);
            if (_logs.Count > MaxLog) _logs.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        void OnGUI()
        {
            if (!_showConsole) return;

            // Команды меняют мир — во время фонового расчёта хода ждут его окончания.
            if (_pendingCmd != null && !GameWorld.IsCalculating)
            {
                string cmd = _pendingCmd;
                _pendingCmd = null;
                _input = "";
                _caret = 0;
                Exec(cmd);
            }

            float h = Screen.height * 0.4f;
            GUI.Box(new Rect(0, 0, Screen.width, h), "");
            GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, h - 20));

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var l in _logs) GUILayout.Label(l);
            GUILayout.EndScrollView();

            // Мигающая каретка в позиции _caret. Показываем |, ~2 Hz.
            bool caretBlink = ((int)(Time.unscaledTime * 2f) & 1) == 0;
            char caretChar  = caretBlink ? '|' : ' ';
            string left  = _caret > 0            ? _input.Substring(0, _caret)              : string.Empty;
            string right = _caret < _input.Length ? _input.Substring(_caret)                 : string.Empty;
            GUILayout.Label($"> {left}{caretChar}{right}");

            GUILayout.EndArea();
        }
    }
}
