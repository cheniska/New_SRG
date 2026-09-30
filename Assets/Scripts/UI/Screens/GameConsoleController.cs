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
    public partial class GameConsoleController : MonoBehaviour
    {
        public SystemViewManager ViewManager;
        public KeyCode ToggleKey = KeyCode.BackQuote;

        public static GameConsoleController Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._showConsole;
        public static event Action OnTurnPerformed;

        private bool   _showConsole;
        private string _input      = "";
        private int    _caret      = 0;
        private string _lastArg    = "";
        private string _pendingCmd = null;
        private Vector2 _scroll;
        private readonly List<string> _logs = new List<string>();
        private const int MaxLog = 200;

        private int _starIndex = 0;
        private Dictionary<string, (Action act, string desc)> _cmds;

        void Awake()
        {
            Instance = this;
            RegisterCommands();
            GameLog.OnEntry += Log;
        }

        void OnDestroy()
        {
            GameLog.OnEntry -= Log;
            if (Instance == this) Instance = null;
        }

        public static void AddEntry(string msg) => GameLog.Add(msg);

        void Start()
        {
            if (!ViewManager) ViewManager = FindFirstObjectByType<SystemViewManager>();
            StartCoroutine(WaitForInit());
        }

        System.Collections.IEnumerator WaitForInit()
        {
            while (GalaxyManager.Instance?.CurrentStar == null) yield return null;
            var list = GalaxyManager.Instance.GeneratedGalaxy.StarsMap.Values.ToList();
            _starIndex = list.IndexOf(GalaxyManager.Instance.CurrentStar);
            if (_starIndex < 0) _starIndex = 0;
            Log($"Viewing: {GalaxyManager.Instance.CurrentStar.Name}");
        }

        void Update()
        {
            // Переключение консоли
            if (Input.GetKeyDown(ToggleKey))
            {
                _showConsole = !_showConsole;
                if (_showConsole) { _input = ""; _caret = 0; }
                return; // пропускаем ввод в кадре переключения
            }

            if (!_showConsole)
            {
                // Навигация по звёздам клавишами +/-
                if (Input.GetKeyDown(KeyCode.Equals)    || Input.GetKeyDown(KeyCode.KeypadPlus))  ChangeSystem(1);
                if (Input.GetKeyDown(KeyCode.Minus)     || Input.GetKeyDown(KeyCode.KeypadMinus)) ChangeSystem(-1);
                return;
            }

            // Закрытие по Escape
            if (Input.GetKeyDown(KeyCode.Escape)) { _showConsole = false; _input = ""; _caret = 0; return; }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                     || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

            // Ctrl+C / Ctrl+V / Ctrl+X — работа с системным буфером обмена.
            // Выделения нет, поэтому Ctrl+C копирует всю строку, Ctrl+X — вырезает.
            if (ctrl)
            {
                if (Input.GetKeyDown(KeyCode.C))
                {
                    GUIUtility.systemCopyBuffer = _input;
                }
                else if (Input.GetKeyDown(KeyCode.V))
                {
                    string paste = GUIUtility.systemCopyBuffer ?? string.Empty;
                    if (paste.Length > 0)
                    {
                        // Отфильтруем управляющие символы (кроме табуляции — превратим её в пробел).
                        var sb = new System.Text.StringBuilder(paste.Length);
                        foreach (char c in paste)
                        {
                            if (c == '\r' || c == '\n') continue; // многострочный paste схлопываем
                            if (c == '\t') { sb.Append(' '); continue; }
                            if (c >= ' ') sb.Append(c);
                        }
                        string clean = sb.ToString();
                        _input = _input.Insert(_caret, clean);
                        _caret += clean.Length;
                    }
                }
                else if (Input.GetKeyDown(KeyCode.X))
                {
                    GUIUtility.systemCopyBuffer = _input;
                    _input = string.Empty;
                    _caret = 0;
                }
            }

            // Каретка: стрелки, Home, End, Delete.
            if (Input.GetKeyDown(KeyCode.LeftArrow))  _caret = Mathf.Max(0, _caret - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) _caret = Mathf.Min(_input.Length, _caret + 1);
            if (Input.GetKeyDown(KeyCode.Home))       _caret = 0;
            if (Input.GetKeyDown(KeyCode.End))        _caret = _input.Length;
            if (Input.GetKeyDown(KeyCode.Delete) && _caret < _input.Length)
                _input = _input.Remove(_caret, 1);

            // Считываем напечатанные символы (работает независимо от IMGUI-фокуса).
            // При Ctrl-хоткеях inputString не даст осмысленных символов (только SYN/ETX/EOT — они < ' ' и отфильтруются),
            // но на всякий случай подстрахуемся флагом.
            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                {
                    if (_caret > 0)
                    {
                        _input = _input.Remove(_caret - 1, 1);
                        _caret--;
                    }
                }
                else if (c == '\r' || c == '\n')
                {
                    if (_pendingCmd == null) _pendingCmd = _input;
                }
                else if (c >= ' ' && !ctrl)
                {
                    _input = _input.Insert(_caret, c.ToString());
                    _caret++;
                }
            }

            _caret = Mathf.Clamp(_caret, 0, _input.Length);
        }

        void ChangeSystem(int dir)
        {
            var g = GalaxyManager.Instance?.GeneratedGalaxy;
            if (g == null || g.StarsMap.Count == 0) { Log("No Galaxy."); return; }
            var list = g.StarsMap.Values.ToList();
            _starIndex = (_starIndex + dir + list.Count) % list.Count;
            var s = list[_starIndex];
            Log($"Viewing: {s.Name} ({_starIndex})");
            GalaxyManager.Instance.SetCurrentStar(s);
        }

    }
}
