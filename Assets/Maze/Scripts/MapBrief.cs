using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Maze
{
    // MISSION BRIEFING (the approved concept, Design/Concepts/2026-10-13-approved-briefing-2qu7v4t0, measured in
    // spec-measured.md): the zone's name and the active modifiers explained on top; the real floor-1 layout and the
    // run's floor plan on the left; the operative and his abilities on the right; BACK, difficulty and LAUNCH below.
    // Numbers are canvas units (1080 high; the concept's px x 1.148). Every value shown comes from the game's data.
    public partial class MapSelect
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static readonly Color BSteel = Hex("#3a5a72"), BGlass = new Color(0.012f, 0.075f, 0.115f, 0.9f), BMuted = Hex("#8aa3b8"), BCyan = Hex("#5fc8f0"),
            BSel = Hex("#429cd1"), BLime = Hex("#c9e940"), BGold = Hex("#ffd05f"), BRed = Hex("#ff6b5b"), BOrange = Hex("#f08a24"), BChip = Hex("#ffcb39"),
            BInk = Hex("#f4f8fb"), BSoft = Hex("#c6d2dd"), BDim = Hex("#7c9ebb");

        Text bTitle, bInfo, bChip, bExplain, bCap, bOpName, bOpRole;
        RectTransform bChipRt, bGoalRow, bTestRt, bOp; Text bTest; RawImage bMap, bPortrait, bHero;
        readonly (Image ring, Image ic, Text nm, RectTransform rt)[] bGoals = new (Image, Image, Text, RectTransform)[5];
        readonly List<RectTransform> bLinks = new();
        readonly (Image edge, Image fill, Image ic, Text nm, Text sub)[] bAbil = new (Image, Image, Image, Text, Text)[3];
        readonly (RectTransform rt, Image edge, Image fill, Image glow, Image lockIc, RectTransform bars, Text nm, Text sub, RectTransform check)[] bTiers =
            new (RectTransform, Image, Image, Image, Image, RectTransform, Text, Text, RectTransform)[3];
        RectTransform bDiffLbl;

        // letter-spaced caps (the concept's tracked labels; the legacy Text has no tracking)
        // tracking: a space at a smaller size between letters (the font has no thin spaces)
        static string Track(string s, int gap = 12)
        {
            string g = $"<size={gap}> </size>", w3 = $"<size={gap * 2}>  </size>";
            var words = s.Split(' '); for (int i = 0; i < words.Length; i++) words[i] = string.Join(g, System.Array.ConvertAll(words[i].ToCharArray(), c => c.ToString()));
            return string.Join(w3, words);
        }
        static Text T(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, string text, int fs, Color c, TextAnchor al, bool bold = false)
        {
            var r = Holo.Rect(parent, name); r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
            var t = Holo.Label(r, text, fs, c, al); if (bold) t.fontStyle = FontStyle.Bold; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.supportRichText = true; return t;
        }
        static RectTransform R(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        { var r = Holo.Rect(parent, name); r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size; return r; }
        // a chamfered glass frame with a thin edge (chamfer scales with ppu: 1 = 15 units)
        static (Image fill, Image edge) Frame(RectTransform r, Color glass, Color edge, float ppu = 1f, bool raycast = false)
        {
            var f = Holo.Img(r, Holo.FillSprite, glass, true, raycast); f.pixelsPerUnitMultiplier = ppu;
            var e = Holo.Img(Holo.Fill(r, "Edge"), Holo.EdgeSprite, edge, true); e.pixelsPerUnitMultiplier = ppu;
            return (f, e);
        }
        // the approved concept's own extracted pixels (Design/Concepts/2026-10-13-original-briefing-assets-4aewixjw), shown at
        // the concept's scale: 1 concept px = 1080/941 canvas units (no redraw; bilinear)
        const float K = 1080f / 941f;
        static Sprite Orig(string n) => Resources.Load<Sprite>("UI/BriefOriginal/" + n);
        static bool Native(Image img, string n, Color? tint = null)
        {
            var sp = Orig(n); if (sp == null) return false;
            img.sprite = sp; img.color = tint ?? Color.white; img.preserveAspect = true; img.rectTransform.sizeDelta = sp.rect.size * K; return true;
        }
        static Texture2D gradL, gradB;
        static Texture2D Grad(bool horizontal) => Holo.Tex(horizontal ? 64 : 4, horizontal ? 4 : 64, (x, y) => new Color(1, 1, 1, Mathf.SmoothStep(1, 0, (horizontal ? x : y) / 63f)));
        void Press(RectTransform r, System.Action a)
        {
            var tap = r.gameObject.AddComponent<Tap>();
            tap.onTap = () => { r.localScale = Vector3.one * 0.97f; Sfx.Click(); Haptics.Light(); a(); };
        }

        void BuildBrief()
        {
            var safe = Holo.SafeRoot(briefRt);
            gradL ??= Grad(true); gradB ??= Grad(false);
            // the bottom band (full bleed)
            var band = Holo.Rect(briefRt, "Band"); band.anchorMin = Vector2.zero; band.anchorMax = new Vector2(1, 0); band.pivot = new Vector2(0.5f, 0); band.sizeDelta = new Vector2(0, 196); band.SetSiblingIndex(safe.GetSiblingIndex());
            Holo.Img(band, Holo.WhiteSprite, new Color(0.01f, 0.05f, 0.085f, 0.82f));
            var bl = R(band, "Line", new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 2)); bl.anchorMin = new Vector2(0, 1); bl.anchorMax = new Vector2(1, 1);
            Holo.Img(bl, Holo.WhiteSprite, Holo.A(BSteel, 0.55f));

            // ---- header
            T(safe, "MB", new Vector2(0, 1), new Vector2(0, 1), new Vector2(86, -32), new Vector2(600, 32), Track("MISSION BRIEFING", 20), 25, BMuted, TextAnchor.MiddleLeft);
            bTitle = T(safe, "Title", new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -58), new Vector2(660, 112), "", 118, BInk, TextAnchor.MiddleLeft, true);
            bTitle.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.5f);
            bInfo = T(safe, "Info", new Vector2(0, 1), new Vector2(0, 1), new Vector2(737, -60), new Vector2(1000, 34), "", 24, Hex("#cfe9f5"), TextAnchor.MiddleLeft);
            bChipRt = R(safe, "Chip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(735, -99), new Vector2(305, 40));
            Frame(bChipRt, new Color(0.17f, 0.085f, 0.03f, 0.92f), BOrange, 2.4f);
            var ci = Holo.Img(R(bChipRt, "Ic", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28, 0), new Vector2(30, 30)), Holo.DotSprite, Hex("#f5a623"));
            if (!Native(ci, "modifier_emblem")) Holo.Img(R((RectTransform)ci.transform, "G", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20)), Holo.Icon("bolt"), Hex("#2a1405"));
            bChip = T(bChipRt, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(54, 0), new Vector2(600, 36), "", 24, BChip, TextAnchor.MiddleLeft, true);
            bExplain = T(safe, "Explain", new Vector2(0, 1), new Vector2(0, 1), new Vector2(737, -146), new Vector2(1180, 30), "", 21, Hex("#c8d6e0"), TextAnchor.MiddleLeft);
            // TEST MODE: a small amber chip, tap = the start floor
            bTestRt = R(safe, "TestFloor", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44, -44), new Vector2(250, 48));
            Frame(bTestRt, new Color(0.17f, 0.11f, 0.02f, 0.92f), Hex("#ffb826"), 2.4f, true);
            bTest = T(bTestRt, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250, 40), "", 22, Hex("#ffb826"), TextAnchor.MiddleCenter, true);
            Press(bTestRt, () => { var d = Sectors[Selected].def; bool gate = d != null && d.endless; int max = gate ? 26 : 5, st = gate ? 5 : 1; int f = Prefs.TestFloor + st; if (f > max) f = 1; Prefs.TestFloor = f; RefreshBriefTest(); });

            // ---- the preview: the real floor layout, and the run's floor plan
            var pv = Holo.Rect(safe, "Preview"); pv.anchorMin = Vector2.zero; pv.anchorMax = Vector2.one; pv.offsetMin = new Vector2(44, 205); pv.offsetMax = new Vector2(-688, -176);
            Frame(pv, BGlass, BSteel);
            var mapRt = Holo.Stretch(Holo.Rect(pv, "Map"), 14, 110, 14, 16);
            mapRt.gameObject.AddComponent<RectMask2D>();
            bMap = Holo.Raw(Holo.Fill(mapRt, "Img"), null, new Color(0.84f, 0.9f, 1f)); bMap.gameObject.AddComponent<CoverUv>();   // a touch darker and cooler, like the concept
            var vb = Holo.Rect(mapRt, "Bottom"); vb.anchorMin = Vector2.zero; vb.anchorMax = new Vector2(1, 0); vb.pivot = new Vector2(0.5f, 0); vb.sizeDelta = new Vector2(0, 60);
            Holo.Raw(vb, gradB, new Color(0.012f, 0.06f, 0.1f, 0.85f));
            Holo.Img(Holo.Fill(mapRt, "Edge"), Holo.EdgeSprite, Holo.A(BSteel, 0.6f), true).pixelsPerUnitMultiplier = 1.6f;
            var capRt = R(mapRt, "Cap", new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(240, 34));
            Holo.Img(capRt, Holo.WhiteSprite, new Color(0.01f, 0.05f, 0.08f, 0.72f));
            bCap = T(capRt, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 30), "", 18, Hex("#cfe9f5"), TextAnchor.MiddleCenter, true);
            bGoalRow = Holo.Rect(pv, "Goals"); bGoalRow.anchorMin = Vector2.zero; bGoalRow.anchorMax = new Vector2(1, 0); bGoalRow.pivot = new Vector2(0.5f, 0); bGoalRow.sizeDelta = new Vector2(0, 104);
            var ring = ProcTex.Circle(128, 4);
            float[] gx = { 0.130f, 0.307f, 0.478f, 0.654f, 0.835f };
            for (int i = 0; i < 4; i++)
            {   // the links between the floors, a small dot at each midpoint
                var ln = Holo.Rect(bGoalRow, "Link" + i); ln.anchorMin = new Vector2(gx[i], 0); ln.anchorMax = new Vector2(gx[i + 1], 0); ln.pivot = new Vector2(0.5f, 0.5f);
                ln.offsetMin = new Vector2(36, 67); ln.offsetMax = new Vector2(-36, 69);
                Holo.Img(ln, Holo.WhiteSprite, Holo.A(Hex("#9fb3c4"), 0.5f)); bLinks.Add(ln);
                var dot = Holo.Rect(bGoalRow, "Dot" + i); dot.anchorMin = dot.anchorMax = new Vector2((gx[i] + gx[i + 1]) / 2, 0); dot.pivot = new Vector2(0.5f, 0.5f); dot.anchoredPosition = new Vector2(0, 68); dot.sizeDelta = new Vector2(10, 10);
                Holo.Img(dot, Holo.DotSprite, Hex("#f3e7b0"));
            }
            for (int i = 0; i < 5; i++)
            {
                var g = Holo.Rect(bGoalRow, "Goal" + i); g.anchorMin = g.anchorMax = new Vector2(gx[i], 0); g.pivot = new Vector2(0.5f, 0); g.anchoredPosition = Vector2.zero; g.sizeDelta = new Vector2(180, 104);
                var rg = Holo.Img(R(g, "Ring", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 68), new Vector2(58, 58)), ring, BCyan);
                var ic = Holo.Img(R(g, "Ic", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 68), new Vector2(42, 42)), Holo.WhiteSprite, BCyan); ic.preserveAspect = true;
                var nm = T(g, "N", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 17), new Vector2(180, 28), "", 22, BInk, TextAnchor.MiddleCenter, true);   // 10 units more air under the icons
                bGoals[i] = (rg, ic, nm, g);
            }

            // ---- the operative
            var op = Holo.Rect(safe, "Operative"); op.anchorMin = new Vector2(1, 0); op.anchorMax = new Vector2(1, 1); op.pivot = new Vector2(1, 0.5f);
            op.offsetMin = new Vector2(-674, 205); op.offsetMax = new Vector2(-43, -180);
            Frame(op, BGlass, BSteel);
            var clip = Holo.Fill(op, "Clip", 4); clip.gameObject.AddComponent<RectMask2D>();
            var por = R(clip, "Portrait", new Vector2(1, 1), new Vector2(1, 1), new Vector2(150, 40), new Vector2(520, 520));
            bPortrait = Holo.Raw(por, null, Color.white);
            var fl = R(clip, "FadeL", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(330, 0)); fl.anchorMax = new Vector2(0, 1); fl.anchorMin = new Vector2(0, 0); fl.sizeDelta = new Vector2(330, 0);
            Holo.Raw(fl, gradL, BGlass);
            var fb = R(clip, "FadeB", new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0, 200), new Vector2(0, 170)); fb.anchorMin = new Vector2(0, 0); fb.anchorMax = new Vector2(1, 0); fb.sizeDelta = new Vector2(0, 170);
            Holo.Raw(fb, gradB, BGlass);
            bOp = op;
            T(op, "OL", new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -22), new Vector2(400, 30), Track("OPERATIVE", 16), 23, BMuted, TextAnchor.MiddleLeft);
            bOpName = T(op, "Name", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -50), new Vector2(560, 64), "", 56, Color.white, TextAnchor.MiddleLeft, true);
            bOpName.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            bOpRole = T(op, "Role", new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -110), new Vector2(560, 36), "", 28, BCyan, TextAnchor.MiddleLeft);
            var ab = R(op, "Abilities", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(619, 214)); ab.anchorMin = new Vector2(0, 0); ab.anchorMax = new Vector2(1, 0); ab.sizeDelta = new Vector2(-12, 214);
            Frame(ab, new Color(0.008f, 0.05f, 0.085f, 0.92f), Holo.A(BSteel, 0.9f));
            T(ab, "L", new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -12), new Vector2(400, 32), Track("ABILITIES", 16), 24, BMuted, TextAnchor.MiddleLeft);
            for (int i = 0; i < 3; i++)
            {
                var t = R(ab, "Ab" + i, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20 + i * 197, -48), new Vector2(184, 146));
                var (fill, edge) = Frame(t, new Color(0.02f, 0.08f, 0.13f, 0.95f), BSteel, 1.6f);
                var ic = Holo.Img(R(t, "Ic", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(46, 46)), Holo.Icon("bolt"), BInk);
                var nm = T(t, "N", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(180, 32), "", 26, BInk, TextAnchor.MiddleCenter, true);
                var sb = T(t, "S", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -121), new Vector2(180, 26), "", 19, BCyan, TextAnchor.MiddleCenter);
                bAbil[i] = (edge, fill, ic, nm, sb);
            }
            var hero = R(op, "VanguardCutout", new Vector2(0, 1), new Vector2(0, 1), new Vector2(37 * K, 6 * K), new Vector2(550 * K, 479 * K));
            bHero = Holo.Raw(hero, Resources.Load<Texture2D>("UI/BriefOriginal/iron-vanguard-hero-cutout"), Color.white);

            // ---- the bottom bar: BACK, difficulty, LAUNCH
            var back = R(safe, "BriefBack", new Vector2(0, 0), new Vector2(0, 0), new Vector2(52, 62), new Vector2(316, 100));
            Frame(back, new Color(0.02f, 0.09f, 0.14f, 0.95f), Hex("#4a6a84"), 1.2f, true);
            var bk = Holo.Img(R(back, "Ic", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60, 0), new Vector2(36, 36)), Holo.Icon("back"), BInk); Native(bk, "back_arrow");
            Holo.Img(R(back, "Div", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110, 0), new Vector2(2, 58)), Holo.WhiteSprite, Holo.A(BSteel, 0.8f));
            T(back, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(140, 0), new Vector2(170, 60), "BACK", 42, BInk, TextAnchor.MiddleLeft, true);
            Press(back, Back);
            bDiffLbl = R(safe, "DiffL", new Vector2(0.5f, 0), new Vector2(0, 0.5f), new Vector2(-439, 162), new Vector2(400, 30));
            T(bDiffLbl, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(200, 30), Track("DIFFICULTY", 12), 22, BMuted, TextAnchor.MiddleLeft);
            var rule = R(bDiffLbl, "Rule", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(178, 0), new Vector2(180, 2));
            Holo.Raw(rule, gradL, Holo.A(BSteel, 0.9f));
            float[] tx = { -442, -149, 115 }, tw = { 272, 242, 254 };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var glow = Holo.Img(R(safe, "TierGlow" + i, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(tx[i] - 26, 32), new Vector2(tw[i] + 52, 136)), Holo.GlowSprite, Holo.A(BSel, 0f));   // behind the button
                var b = R(safe, "Tier" + i, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(tx[i], 58), new Vector2(tw[i], 84));
                var (fill, edge) = Frame(b, new Color(0.02f, 0.08f, 0.13f, 0.95f), BSteel, 1.5f, true);
                var lk = Holo.Img(R(b, "Lock", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(34, 34)), Holo.Icon("lock"), BSoft); Native(lk, "lock_small");
                var bars = R(b, "Bars", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(34, 34));
                if (!Native(Holo.Img(bars, Holo.WhiteSprite, Color.white), "difficulty_bars"))
                    for (int j = 0; j < 3; j++) Holo.Img(R(bars, "B" + j, new Vector2(0, 0), new Vector2(0, 0), new Vector2(j * 12, 0), new Vector2(8, j == 1 ? 32 : 20)), Holo.WhiteSprite, Color.white);
                var nm = T(b, "N", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(84, 13), new Vector2(200, 34), Meta.Tiers[i], 28, BInk, TextAnchor.MiddleLeft, true);
                var sb = T(b, "S", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(84, -17), new Vector2(200, 26), "", 18, BDim, TextAnchor.MiddleLeft);
                var chk = R(b, "Check", new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40, 0), new Vector2(38, 38));
                var cb = Holo.Img(chk, ring, BSel); if (!Native(cb, "selected_badge")) Holo.Img(R(chk, "C", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22)), Holo.Icon("check"), BInk);
                Press(b, () => PickTier(k));
                bTiers[i] = (b, edge, fill, glow, lk, bars, nm, sb, chk);
            }
            Holo.Img(R(safe, "LaunchGlow", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 15), new Vector2(554, 197)), Holo.GlowSprite, Holo.A(BLime, 0.3f));   // behind the button
            var launch = R(safe, "Launch", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-54, 55), new Vector2(474, 117));
            Frame(launch, BLime, Hex("#e4ff7a"), 1.2f, true);
            var pl = Holo.Img(R(launch, "Play", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(124, 0), new Vector2(64, 64)), Holo.Icon("play"), Hex("#091904")); Native(pl, "launch_triangle");
            T(launch, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(172, 0), new Vector2(280, 80), "LAUNCH", 64, Hex("#091904"), TextAnchor.MiddleLeft, true);
            Press(launch, Launch);
        }

        void RefreshBriefTest()
        {
            if (bTestRt == null) return; bTestRt.gameObject.SetActive(Prefs.TestMode);
            bTest.text = $"TEST  ·  FLOOR {Prefs.TestFloor}{(Prefs.TestPower ? "  ·  RP" : "")}";
        }

        // the modifiers this drop will run with (the same rules as ZoneMods.Setup), each with its own explanation
        static readonly string[] ModDesc =
        {
            "Enemies move 25% faster.", "Drones take 60% more to kill.", "40% more enemies and biomass.", "Standing still for 2 s burns 2 HP/s.",
            "An elite each floor; its kill is a free card.", "Darkness: only lit enemies can be targeted.",
            "Your projectile hits deal 50% more damage; you take 50% more damage.", "Nests and bosses regenerate 1%/s.",
            "Telegraphed lightning strikes.", "Enemy deaths burst near you.",
        };
        static string Sentence(string s) { s = s.Trim(); return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant() + (s.EndsWith(".") ? "" : "."); }
        static (List<string> names, List<string> descs) BriefMods(SectorDef s, int difficulty, bool hot)
        {
            var n = new List<string>(); var d = new List<string>(); var seen = new HashSet<int>();
            if (s == null || s.endless) return (n, d);
            if (Ops.WeeklyOn) { var (wa, wb) = Ops.WeeklyPair; foreach (var m in new[] { wa, wb }) if (seen.Add(m)) { n.Add(ZoneMods.Names[m]); d.Add(ModDesc[m]); } }
            if (s.modifiers != null)
            {
                int c = Mathf.Min(ZoneMods.CountFor(difficulty, hot), s.modifiers.Length);
                for (int i = 0; i < c; i++)
                {
                    var (mut, _) = ZoneMods.Lookup(s.modifiers[i]); var nm = ZoneMods.NameOf(s.modifiers[i]).ToUpperInvariant();
                    int colon = s.modifiers[i].IndexOf(':');
                    if (mut >= 0 && !seen.Add(mut)) continue;
                    n.Add(nm); d.Add(mut >= 0 ? ModDesc[mut] : colon > 0 ? Sentence(s.modifiers[i].Substring(colon + 1)) : "");
                }
            }
            if (hot) { n.Add("HOT ZONE"); d.Add(Sentence($"{Ops.Mutators[Ops.HotMutator]}: {Ops.MutatorText[Ops.HotMutator]}, x2 {Ops.MaterialNames[Ops.HotMaterial]}")); }
            return (n, d);
        }

        void OpenBrief()
        {
            var s = Sectors[Selected];
            if (s.locked) { Sfx.Deny(); Haptics.Warn(); return; }
            if (s.def != null && Meta.TierGateFor(s.def, Meta.Difficulty) != null) Meta.Difficulty = 0;
            bTitle.text = s.name; bTitle.fontSize = 118;
            float pw = bTitle.preferredWidth; if (pw > 640) bTitle.fontSize = Mathf.FloorToInt(118 * 640 / pw);
            // the preview: the zone's floor-1 layout rendered from the game (BriefArt), else the cover art, labelled
            Texture2D tex = s.map == 1 ? Resources.Load<Texture2D>("UI/arena_escape") : s.def != null ? Resources.Load<Texture2D>("UI/Arena/" + s.def.id) : null;
            bool real = tex != null;
            if (!real) tex = Resources.Load<Texture2D>("UI/Sectors/" + s.art);
            bMap.texture = tex; if (tex != null) bMap.GetComponent<CoverUv>().aspect = (float)tex.width / tex.height;
            bCap.text = !real ? "ILLUSTRATIVE" : s.map == 1 ? "TEST MAP LAYOUT" : "FLOOR 1 LAYOUT";
            // the floor plan
            if (s.map == 1) { Game.PendingPlan = null; bGoalRow.gameObject.SetActive(false); }
            else
            {
                Game.PendingPlan = Game.NewPlan(s.def);   // drawn now so the briefing shows the run's shape
                bGoalRow.gameObject.SetActive(true);
                for (int i = 0; i < 5; i++)
                {
                    var g = Game.PendingPlan[i]; var gi = bGoals[i];
                    var col = g == Game.Goal.Boss ? BRed : g == Game.Goal.Hunt ? BGold : BCyan;
                    string on = g == Game.Goal.Purge ? "purge_badge" : g == Game.Goal.Hunt ? "hunt_badge" : g == Game.Goal.Hold ? "hold_badge" : g == Game.Goal.Boss ? "boss_badge" : g == Game.Goal.Survive ? "survive" : null;
                    bool orig = on != null && Native(gi.ic, on);   // the concept's own badge, at its size
                    if (!orig)
                    {
                        var spr = Holo.Art("goal_" + Game.GoalNames[(int)g].ToLower() + "_mono");
                        gi.ic.sprite = spr != null ? spr : Holo.Icon(Game.GoalIcons[(int)g]); gi.ic.color = col; gi.ic.rectTransform.sizeDelta = new Vector2(42, 42);
                    }
                    gi.ring.enabled = !orig; gi.ring.color = Holo.A(col, 0.9f); gi.nm.text = Game.GoalNames[(int)g]; gi.nm.color = g == Game.Goal.Boss ? BRed : g == Game.Goal.Hunt ? BGold : BInk;
                }
            }
            RefreshTiers(); RefreshOperative(); RefreshBriefTest();
            SetMode(Mode.Brief); Sfx.Open(); Haptics.Light();
        }

        // the header's power and modifiers follow the difficulty
        void RefreshBriefHeader()
        {
            var s = Sectors[Selected]; var def = s.def;
            string what = s.map == 1 ? "ESCAPE THE FLOOR" : def != null && def.endless ? "ENDLESS FLOORS" : $"{Game.Floors} FLOORS";
            bInfo.text = $"{Track(what, 9)}<size=24>    •    </size>{Track("RECOMMENDED POWER", 9)}  <b><color=#ffffff>{s.Rec:N0}</color></b>";
            var (names, descs) = BriefMods(def, Meta.Difficulty, def != null && def.id == hotId);
            bool any = names.Count > 0;
            bChipRt.gameObject.SetActive(any); bExplain.gameObject.SetActive(any);
            if (!any) return;
            bChip.text = string.Join(" + ", names);
            bChipRt.sizeDelta = new Vector2(Mathf.Min(1150, bChip.preferredWidth + 78), 40);
            var parts = new List<string>(); for (int i = 0; i < names.Count; i++) if (descs[i] != "") parts.Add($"<b>{names[i]}:</b> {char.ToLowerInvariant(descs[i][0])}{descs[i].Substring(1)}");
            bExplain.text = string.Join("  ", parts);   // each modifier with its own effect (FRENZY: speed; GLASS CANNON: damage)
            bExplain.fontSize = 21; float w = bExplain.preferredWidth; float room = ((RectTransform)bExplain.transform.parent).rect.width - 737 - 40;
            if (room > 200 && w > room) bExplain.fontSize = Mathf.Max(15, Mathf.FloorToInt(21 * room / w));
        }

        void RefreshTiers()
        {
            var def = Sectors[Selected].def;
            bool show = def != null && !def.endless;
            if (bDiffLbl != null) bDiffLbl.gameObject.SetActive(show);
            for (int i = 0; i < 3; i++)
            {
                var b = bTiers[i]; if (b.rt == null) continue; b.rt.gameObject.SetActive(show); b.glow.gameObject.SetActive(show);
                if (!show) continue;
                var gate = Meta.TierGateFor(def, i); bool sel = Meta.Difficulty == i, locked = gate != null;
                b.edge.color = sel ? BSel : Holo.A(BSteel, locked ? 0.8f : 1f);
                b.fill.color = sel ? Hex("#0d2c40") : new Color(0.02f, 0.08f, 0.13f, 0.95f);
                b.glow.color = Holo.A(BSel, sel ? 0.35f : 0f);
                b.lockIc.gameObject.SetActive(locked); b.bars.gameObject.SetActive(!locked);
                foreach (var img in b.bars.GetComponentsInChildren<Image>()) img.color = sel ? Color.white : Holo.A(Color.white, 0.7f);
                b.nm.color = sel ? Color.white : BSoft;
                int st = 0; for (int k = 0; k < 3; k++) if (Progress.D.stars.Contains(Meta.StarKey(def, i, k))) st++;
                b.sub.text = locked ? gate : $"{Meta.RecommendedAt(def, i):N0}" + (st > 0 ? $"  <color=#ffd05f>{new string('★', st)}</color>" : "");
                b.sub.color = locked ? BDim : sel ? Hex("#3fa9f5") : BCyan;
                b.check.gameObject.SetActive(sel);
            }
            if (bInfo != null) RefreshBriefHeader();
        }

        // the OPERATIVE frame follows the Squad pick
        void RefreshOperative()
        {
            if (bOpName == null) return;
            // the Iron Vanguard: the concept's original cutout (its torso ends where the concept's ABILITIES panel starts,
            // source y 572; on a shorter phone panel it scales down so that edge still sits behind the ABILITIES panel)
            bool cut = Prefs.Vanguard && bHero.texture != null;
            bHero.gameObject.SetActive(cut); bPortrait.gameObject.SetActive(!cut);
            FitHero();
            var tex = Prefs.HeroPortrait(); if (!cut && tex != null) {
                bool bust = !Prefs.Scout;   // a painted bust, else the Scout's full-body render (3:4): lower, under the name
                float h = bust ? 520 : 470; bPortrait.texture = tex; bPortrait.rectTransform.sizeDelta = new Vector2(bust ? h : h * 0.75f, h);
                bPortrait.rectTransform.anchoredPosition = bust ? new Vector2(150, 40) : new Vector2(0, -150);
            }
            bOpName.text = Prefs.HeroName; bOpName.fontSize = 56; float pw = bOpName.preferredWidth; if (pw > 296) bOpName.fontSize = Mathf.FloorToInt(56 * 296 / pw);   // the concept's name width (257 concept px)
            bOpRole.text = Prefs.HeroRole.Replace("  //  ", " / ");
            // DASH, the hero's skill (its real cooldown at his ability rank), then the grenade (a run upgrade card)
            float dash = Mathf.Max(0.5f, game.cfg.player.dashCooldown - Meta.BootsDashCut);
            SetAbility(0, "DASH", "bolt", $"{dash:0.#} s COOLDOWN", true);
            SetAbility(1, Abilities.Name, Abilities.Vanguard ? "rifle" : Abilities.Scout ? "person" : "eye", $"{Abilities.BaseCooldown:0.#} s COOLDOWN", true);
            SetAbility(2, "GRENADE", "lock", "UPGRADE CARD", false);
        }
        // (each frame while the briefing shows: the safe area settles the panel's height a frame after it opens)
        void FitHero()
        {
            if (bHero == null || !bHero.gameObject.activeSelf) return;
            float h = bOp.rect.height, edge = h - 6 - 214, need = (572 - 151) * K;   // the ABILITIES panel's top; the cutout's top to its torso edge
            float k = h > 1 ? Mathf.Clamp(edge / need, 0.6f, 1f) : 1f;
            var hr = bHero.rectTransform; hr.sizeDelta = new Vector2(550 * K, 479 * K) * k; hr.anchoredPosition = new Vector2(37 * K + 550 * K * (1 - k), 6 * K);
        }
        void SetAbility(int i, string name, string icon, string sub, bool ready)
        {
            var a = bAbil[i];
            a.nm.text = name; a.sub.text = sub;
            a.ic.sprite = Holo.Icon(ready ? icon : "lock"); a.ic.color = ready ? Color.white : BSoft; a.ic.preserveAspect = true; a.ic.rectTransform.sizeDelta = new Vector2(46, 46);
            if (!ready) Native(a.ic, "lock"); else if (icon == "bolt" && i == 0) Native(a.ic, "dash");
            a.edge.color = ready && i == 0 ? BSel : Holo.A(BSteel, 0.95f);
            a.nm.color = ready ? Color.white : BSoft; a.sub.color = ready ? Hex("#80cef9") : BDim;
        }

        void RefreshLoadout() => RefreshOperative();
    }
}
