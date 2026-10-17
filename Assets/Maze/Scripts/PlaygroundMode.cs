using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Maze
{
    // PLAYGROUND (2026-10-16, Manuel: "a playground where I can spawn any enemy, and use the character in a map to try
    // different things"). A real game session, not the model Sandbox: the Iron Vanguard with his current kit and stats on
    // Cryo Station's first room (cryo-a: raised islands for cover, the gun and flamethrower hero turrets, the ice ring), in the
    // CryoSoft look with every readability / feel effect. Waves and the floor goal are off; nothing spawns unless you ask.
    // Only with Settings > TEST MODE on (its runs never write progress, currency or unlocks); a PLAYGROUND button shows on the
    // menu then. The picker is a collapsible panel on the right edge (its own canvas; nothing in the HUD files):
    //   every roster enemy (with an ELITE toggle and +1 / +5) and every data boss (+1), spawned ~6 m in front of the hero
    //   WAVE (10 mixed Cryo enemies round you), CLEAR, FREEZE, GOD, SKILLS (no cooldown), 0.5x time, LV - / + (card picks), EXIT
    public class PlaygroundMode : MonoBehaviour
    {
        public static bool Active;
        public static PlaygroundMode I;
        static bool god, freeze, skills, slow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var go = new GameObject("PlaygroundMode"); DontDestroyOnLoad(go); I = go.AddComponent<PlaygroundMode>(); }

        Game game; Canvas canvas; GameObject menuBtn, tab, panel; TMP_Text lvLabel, tabLabel; readonly List<(Toggle t, int ti)> elite = new();
        TMP_FontAsset font;

        // ---------------------------------------------------------------- enter / leave
        public static bool Enter(Game g)
        {
            if (!Prefs.TestMode || g == null) return false;
            var def = SectorDefs.Get("cryo-station"); if (def == null) return false;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart();
            Active = true; god = false; freeze = false; skills = false; slow = false;
            g.swarm.Clear(); if (I != null) I.Show(true);
            Debug.Log("Playground: entered (Test mode, save frozen)");
            return true;
        }
        public static void Leave(Game g)
        {
            Active = false; Time.timeScale = 1; if (g != null) { g.godMode = false; g.ToMenu(); }
            Debug.Log("Playground: left");
        }

        void LateUpdate()
        {
            if (game == null) { game = FindAnyObjectByType<Game>(); if (game == null) return; }
            if (canvas == null) Build();
            bool onMenu = game.state == Game.State.Menu && !game.run;
            if (menuBtn != null) menuBtn.SetActive(onMenu && Prefs.TestMode && !Active);
            if (Active && !game.run) { Active = false; Time.timeScale = 1; Show(false); }
            if (!Active) { if (tab != null && tab.activeSelf) Show(false); return; }
            game.godMode = god;
            if (skills) Abilities.Cd = 0;
            if (freeze) foreach (var e in game.swarm.enemies) if (!e.dead) e.frozen = Mathf.Max(e.frozen, .25f);
            if (slow && game.state == Game.State.Playing && Time.timeScale > .49f) Time.timeScale = .5f;
            if (lvLabel != null) lvLabel.text = "LV " + game.level;
        }

        // ---------------------------------------------------------------- spawning
        Vector2 Ahead(float dist)
        {
            var p = game.player; var dir = new Vector2(Mathf.Sin(p.facing), Mathf.Cos(p.facing)); var at = p.p + dir * dist;
            // the nearest open cell to that point
            Vector2 best = at; float bd = 1e9f;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) { var c = MazeMap.Center(x, z); float d = (c - at).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }
        void Spawn(int ti, int n, bool asElite)
        {
            for (int i = 0; i < n; i++)
            {
                var at = Ahead(6f) + new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(-1.2f, 1.2f)) * (n > 1 ? 1 : 0);
                var e = game.DebugSpawn(ti, at); if (e == null) continue; e.spawnT = 0;
                if (asElite) Elites.MakeElite(e);
            }
        }
        void SpawnBoss(string name)
        {
            var e = BossBrain.Spawn(game, name, Ahead(7f), false);
            if (e == null) game.hud.ShowBanner(name.ToUpper() + ": NO BODY");
            else if (e.brain != null) e.brain.Intro(1.2f);
        }
        void Wave()
        {
            var mix = new[] { "grunt", "grunt", "grunt", "grunt", "grunt", "grunt", "leaper", "spitter", "tick", "carapace" };
            for (int i = 0; i < mix.Length; i++)
            {
                int ti = game.swarm.TypeByName(mix[i]); if (ti < 0) continue;
                float a = i / (float)mix.Length * Mathf.PI * 2; var at = game.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8.5f;
                Vector2 best = at; float bd = 1e9f;
                for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) { var c = MazeMap.Center(x, z); float d = (c - at).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
                var e = game.DebugSpawn(ti, best); if (e != null) e.spawnT = 0;
            }
        }
        void Clear() { foreach (var e in game.swarm.enemies.ToArray()) game.KillEnemy(e); }
        void LevelBy(int d)
        {
            if (d > 0 && game.level < Game.MaxLevel)
            {   // a level up: the card pick shows as in a run
                game.level++; var f = typeof(Game).GetField("pendingLevels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); f?.SetValue(game, (int)f.GetValue(game) + 1);
            }
            else if (d < 0 && game.level > 1) game.level--;
        }
        static List<string> bossNames;
        static List<string> BossNames()
        {
            if (bossNames != null) return bossNames; bossNames = new List<string>();
            var ta = Resources.Load<TextAsset>("Design/bosses"); if (ta == null) return bossNames;
            foreach (Match m in Regex.Matches(ta.text, "\"name\"\\s*:\\s*\"([^\"]+)\"")) if (!bossNames.Contains(m.Groups[1].Value)) bossNames.Add(m.Groups[1].Value);
            return bossNames;
        }
        // (checks) spawn every roster enemy and every boss once; returns the names that failed
        public static List<string> SpawnAllOnce(Game g, bool bosses)
        {
            var bad = new List<string>(); if (I == null) return bad; I.game = g;
            if (!bosses) for (int i = 0; i < g.cfg.enemyTypes.Length; i++)
            {
                var t = g.cfg.enemyTypes[i]; if (t.ai == "boss") continue;
                try { var e = g.DebugSpawn(i, I.Ahead(6f)); if (e == null || e.view == null) bad.Add(t.name + (e == null ? " (no enemy)" : " (no view)")); }
                catch (System.Exception ex) { bad.Add(t.name + " (" + ex.GetType().Name + ")"); }
            }
            else foreach (var n in BossNames())
            {
                try { var e = BossBrain.Spawn(g, n, I.Ahead(7f), false); if (e == null) bad.Add(n + " (no body)"); }
                catch (System.Exception ex) { bad.Add(n + " (" + ex.GetType().Name + ")"); }
            }
            return bad;
        }

        // ---------------------------------------------------------------- the picker (its own canvas, phone-sized)
        public void Show(bool on) { if (tab != null) tab.SetActive(on); if (panel != null) panel.SetActive(on && open); }
        bool open = true;
        void Build()
        {
            font = Resources.Load<TMP_FontAsset>("BlueUI/Fonts/Display") ?? TMP_Settings.defaultFontAsset;
            var go = new GameObject("PlaygroundCanvas"); go.layer = 5; go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1;
            go.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); DontDestroyOnLoad(es); }
            var root = (RectTransform)go.transform;
            // the menu's PLAYGROUND button (Test mode only)
            menuBtn = Btn(root, "PLAYGROUND", new Color(.98f, .72f, .18f), () => Enter(game)).gameObject;
            Place(menuBtn.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(40, 40), new Vector2(330, 92));
            // the collapse tab and the panel
            var tabBtn = Btn(root, "PLAYGROUND", new Color(.12f, .2f, .42f, .92f), () => { open = !open; panel.SetActive(open); tabLabel.text = open ? "HIDE" : "PLAYGROUND"; });
            tab = tabBtn.gameObject; tabLabel = tab.GetComponentInChildren<TMP_Text>(); tabLabel.text = "HIDE";
            Place(tab.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-24, -150), new Vector2(230, 64));
            panel = Panel(root, new Color(.04f, .07f, .16f, .9f)); var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1, .36f); prt.anchorMax = new Vector2(1, 1); prt.pivot = new Vector2(1, 1); prt.sizeDelta = new Vector2(540, -230); prt.anchoredPosition = new Vector2(-24, -222);
            // controls: two rows of toggles / actions, the level stepper
            var ctr = new GameObject("Controls", typeof(RectTransform)); ctr.transform.SetParent(panel.transform, false);
            var crt = (RectTransform)ctr.transform; crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(.5f, 1); crt.sizeDelta = new Vector2(-20, 222); crt.anchoredPosition = new Vector2(0, -10);
            var grid = ctr.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(122, 66); grid.spacing = new Vector2(8, 8); grid.childAlignment = TextAnchor.UpperCenter;
            Tog(crt, "GOD", v => god = v); Tog(crt, "FREEZE", v => freeze = v); Tog(crt, "SKILLS", v => skills = v); Tog(crt, "0.5x", v => { slow = v; if (!v) Time.timeScale = 1; }); Tog(crt, "CHIBI", v => HeroChibi.On = v).isOn = HeroChibi.On; Tog(crt, "CHIBI 3D", v => { HeroChibi.ModelOn = v; game.player.RebuildHero(game.cfg); }).SetIsOnWithoutNotify(HeroChibi.ModelOn);
            Btn(crt, "WAVE", new Color(.2f, .55f, .9f), Wave); Btn(crt, "CLEAR", new Color(.75f, .25f, .3f), Clear);
            Btn(crt, "LV -", new Color(.25f, .32f, .55f), () => LevelBy(-1)); Btn(crt, "LV +", new Color(.25f, .32f, .55f), () => LevelBy(1));
            lvLabel = Label(crt, "LV 1", 28); Btn(crt, "EXIT", new Color(.5f, .52f, .6f), () => Leave(game));
            // the list
            var sv = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D)); sv.transform.SetParent(panel.transform, false);
            var srt = (RectTransform)sv.transform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = new Vector2(10, 10); srt.offsetMax = new Vector2(-10, -238);
            sv.GetComponent<Image>().color = new Color(0, 0, 0, .25f);
            var content = new GameObject("Content", typeof(RectTransform)); content.transform.SetParent(sv.transform, false);
            var cr = (RectTransform)content.transform; cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(.5f, 1); cr.sizeDelta = new Vector2(0, 0);
            var vl = content.AddComponent<VerticalLayoutGroup>(); vl.spacing = 6; vl.childControlHeight = false; vl.childForceExpandHeight = false; vl.padding = new RectOffset(6, 6, 6, 6);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scr = sv.GetComponent<ScrollRect>(); scr.content = cr; scr.horizontal = false; scr.scrollSensitivity = 30;
            Header(cr, "ENEMIES");
            var types = game.cfg.enemyTypes;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i].ai == "boss" || types[i].nest) continue; int ti = i;
                var row = Row(cr); Label(row, types[i].name.ToUpper(), 26, 190);
                var et = Tog(row, "ELITE", null, 112); elite.Add((et, ti));
                Btn(row, "+1", new Color(.2f, .55f, .9f), () => Spawn(ti, 1, et.isOn), 84); Btn(row, "+5", new Color(.2f, .45f, .8f), () => Spawn(ti, 5, et.isOn), 84);
            }
            Header(cr, "BOSSES");
            foreach (var n in BossNames()) { var nm = n; var row = Row(cr); Label(row, nm.ToUpper(), 24, 360); Btn(row, "+1", new Color(.85f, .4f, .2f), () => SpawnBoss(nm), 84); }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;   // the UI layer
            Show(false);
        }
        // ---- small UI helpers (rounded-free, flat: phone-sized touch targets ~64 px at 1080p)
        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size) { r.anchorMin = r.anchorMax = r.pivot = anchor; r.anchoredPosition = pos; r.sizeDelta = size; }
        static GameObject Panel(Transform parent, Color c) { var g = new GameObject("Panel", typeof(RectTransform), typeof(Image)); g.transform.SetParent(parent, false); g.GetComponent<Image>().color = c; return g; }
        RectTransform Row(Transform parent)
        {
            var g = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup)); g.transform.SetParent(parent, false);
            g.GetComponent<Image>().color = new Color(1, 1, 1, .05f); var h = g.GetComponent<HorizontalLayoutGroup>(); h.spacing = 8; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = false; h.childForceExpandWidth = false; h.padding = new RectOffset(10, 6, 4, 4);
            var rt = (RectTransform)g.transform; rt.sizeDelta = new Vector2(0, 70); return rt;
        }
        void Header(Transform parent, string s) { var l = Label(parent, s, 24); l.color = new Color(.55f, .85f, 1f); ((RectTransform)l.transform).sizeDelta = new Vector2(480, 44); }
        TMP_Text Label(Transform parent, string s, float size, float width = 120)
        {
            var g = new GameObject("Label", typeof(RectTransform)); g.transform.SetParent(parent, false); var t = g.AddComponent<TextMeshProUGUI>();
            t.font = font; t.text = s; t.fontSize = size; t.color = Color.white; t.alignment = TextAlignmentOptions.MidlineLeft; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            ((RectTransform)g.transform).sizeDelta = new Vector2(width, 60); return t;
        }
        Button Btn(Transform parent, string s, Color c, System.Action onClick, float width = 122)
        {
            var g = new GameObject(s, typeof(RectTransform), typeof(Image), typeof(Button)); g.transform.SetParent(parent, false);
            g.GetComponent<Image>().color = c; ((RectTransform)g.transform).sizeDelta = new Vector2(width, 60);
            var b = g.GetComponent<Button>(); b.onClick.AddListener(() => onClick?.Invoke());
            var t = Label(g.transform, s, 26, width); t.alignment = TextAlignmentOptions.Center; var tr = (RectTransform)t.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            return b;
        }
        Toggle Tog(Transform parent, string s, System.Action<bool> onChange, float width = 122)
        {
            var g = new GameObject(s, typeof(RectTransform), typeof(Image), typeof(Toggle)); g.transform.SetParent(parent, false);
            var img = g.GetComponent<Image>(); img.color = new Color(.22f, .26f, .38f); ((RectTransform)g.transform).sizeDelta = new Vector2(width, 60);
            var tg = g.GetComponent<Toggle>(); tg.isOn = false; tg.targetGraphic = img;
            var t = Label(g.transform, s, 24, width); t.alignment = TextAlignmentOptions.Center; var tr = (RectTransform)t.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            tg.onValueChanged.AddListener(v => { img.color = v ? new Color(.2f, .7f, .45f) : new Color(.22f, .26f, .38f); onChange?.Invoke(v); });
            return tg;
        }
    }
}
