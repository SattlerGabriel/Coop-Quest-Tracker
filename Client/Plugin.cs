using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using UnityEngine;

namespace CoopQuestTracker;

public class QuestEntry
{
    public string Trader;
    public string Name;
    public string Status;
}

public class PlayerQuests
{
    public string Nickname;
    public int Level;
    public List<QuestEntry> Quests;
}

[BepInPlugin("com.boombah.coopquesttracker", "Coop Quest Tracker", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private static readonly (string Status, string Label, string Color)[] Statuses =
    [
        ("shared", "Shared", "#7CFC7C"),
        ("missing", "Missing", "#FFD84D"),
        ("locked", "Locked", "#FF7B7B"),
        ("completed", "Completed", "#8FB8FF")
    ];

    internal static Plugin Instance;
    internal static MenuScreen Menu;

    private ConfigEntry<bool> _show;
    private ConfigEntry<KeyboardShortcut> _toggleKey;
    private ConfigEntry<float> _scale;
    private ConfigEntry<int> _height;
    private List<PlayerQuests> _players;
    private string _error;
    private bool _loading;
    private Rect _window = new(20, 120, 420, 300);
    private Vector2 _scroll;
    private GUIStyle _wrap;

    private void Awake()
    {
        Instance = this;
        _show = Config.Bind("General", "Show panel", true, "Show the shared quests panel on the main menu");
        _toggleKey = Config.Bind("General", "Toggle key", new KeyboardShortcut(KeyCode.F7), "Show or hide the panel");
        _scale = Config.Bind("General", "Window scale", 1f, new ConfigDescription("Size of the panel", new AcceptableValueRange<float>(0.5f, 2.5f)));
        _height = Config.Bind("General", "Window height", 300, new ConfigDescription("Height of the panel before scaling", new AcceptableValueRange<int>(150, 1200)));
        new MenuScreenShowPatch().Enable();
    }

    private void Update()
    {
        if (_toggleKey.Value.IsDown())
        {
            _show.Value = !_show.Value;
        }
    }

    internal async void Refresh()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            _players = JsonConvert.DeserializeObject<List<PlayerQuests>>(await RequestHandler.GetJsonAsync("/coopquesttracker/players"));
            _error = null;
        }
        catch (Exception e)
        {
            _error = "Couldn't reach the server mod. Is CoopQuestTracker installed on the server?";
            Logger.LogError(e);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnGUI()
    {
        if (!_show.Value || Menu == null || !Menu.gameObject.activeInHierarchy)
        {
            return;
        }

        _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(_scale.Value, _scale.Value, 1f));
        _window.height = _height.Value;
        _window = GUILayout.Window(GetHashCode(), _window, DrawWindow, "Coop Quests (online players)");
        GUI.matrix = matrix;
    }

    private void DrawWindow(int id)
    {
        if (GUILayout.Button(_loading ? "Refreshing..." : "Refresh"))
        {
            Refresh();
        }

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_height.Value - 60));
        if (_error != null)
        {
            GUILayout.Label(_error, _wrap);
        }
        else if (_players == null || _players.Count == 0)
        {
            GUILayout.Label(_loading ? "Loading..." : "No other players online.", _wrap);
        }
        else
        {
            foreach (var player in _players)
            {
                GUILayout.Label($"<b>{player.Nickname}</b> (lvl {player.Level})", _wrap);
                if (player.Quests.Count == 0)
                {
                    GUILayout.Label("  You have no active quests.", _wrap);
                }

                foreach (var (status, label, color) in Statuses)
                {
                    var quests = player.Quests.Where(q => q.Status == status).ToList();
                    if (quests.Count == 0)
                    {
                        continue;
                    }

                    GUILayout.Label($"  <color={color}>{label}</color>", _wrap);
                    foreach (var quest in quests)
                    {
                        GUILayout.Label(quest.Trader == null ? $"    • {quest.Name}" : $"    • {quest.Trader} - {quest.Name}", _wrap);
                    }
                }

                GUILayout.Space(6);
            }
        }

        GUILayout.EndScrollView();
        GUI.DragWindow();
    }
}

public class MenuScreenShowPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(MenuScreen).GetMethods().First(m => m.Name == nameof(MenuScreen.Show) && m.GetParameters().Length == 3);

    [PatchPostfix]
    private static void Postfix(MenuScreen __instance)
    {
        Plugin.Menu = __instance;
        Plugin.Instance.Refresh();
    }
}
