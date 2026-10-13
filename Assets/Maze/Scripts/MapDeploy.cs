using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Maze
{
    // SELECT DROP ZONE (the approved concept, Design/Concepts/2026-10-13-approved-deploy-tdw8mydk, measured in
    // spec-measured.md): the title and the star track on top with ROUTE / BACK; the live planet with its zone markers;
    // the destination cards below; the picked zone's panel on the right (power, best attempt, threat, the weekly
    // challenge) and BRIEFING. Picking a zone only rewrites the panel's values (no rebuild).
    public partial class MapSelect
    {
        string hotId;   // today's Hot Zone (orange pin)
        RectTransform testBadge; Text testBadgeT;
        readonly List<Image> newTags = new();   // a gold dot on an unlocked, never-opened zone pin
        readonly HashSet<string> seenCache = new();
        static bool Seen(SectorDef d) => PlayerPrefs.GetInt("ms.seen." + d.id, 0) == 1;

        RawImage planetRaw; RectTransform planetArea, pinLabel; Text pinLabelT; ScrollRect cardStrip; float stripTarget, stripT;
        readonly List<(RectTransform rt, Image dot, Image ring, Image lockIc, CanvasGroup cg, Image disc, Image diamond, Image reticle)> pins = new();
        readonly List<(RectTransform rt, Image glow, Image edge, RawImage art, Image[] stars, RectTransform lockBadge)> cards = new();
        Text subtitle, secName, lockLine, dropCount, dropSector, routeToast, dPower, dBest, dBlurb, dWeekly, dWeeklyTip;
        RawImage secArt; readonly List<Image> threatPips = new(); readonly Image[] dBestStars = new Image[3];
        RectTransform deployBtn, lockedBox, dWeeklyChip, dWeeklyTipRt; Image dWeeklyEdge, dWeeklyFill, dWeeklyIc, dWeeklyGlyph;
        const float CardStep = 238, CardW = 224, CardH = 262;
        // five-pointed stars (filled = earned, outline = not yet), drawn once
        static Sprite starFull, starLine;
        static float StarSdf(float x, float y, float r)
        {   // inside test against a 5-point star: the max over the 5 edge half-planes of the concave star, approximated per sector
            float a = Mathf.Atan2(x, y), d = Mathf.Sqrt(x * x + y * y);
            float k = Mathf.PI / 5f, m = Mathf.Repeat(a + k, 2 * k) - k;   // the angle within a point's sector
            float ri = r * 0.45f, t = Mathf.Abs(m) / k;   // 0 at the tip, 1 between the points
            float edge = Mathf.Lerp(r, ri, t);   // radius of the outline in this direction (linear between tip and notch)
            return d - edge;
        }
        static Sprite StarSprite(bool full)
        {
            var t = Holo.Tex(64, 64, (x, y) =>
            {
                float px = x - 31.5f, py = y - 30.5f, sd = StarSdf(px, py, 30f);
                float a = full ? Mathf.Clamp01(0.5f - sd) : Mathf.Clamp01(0.5f - sd) * Mathf.Clamp01(sd + 3.5f);
                return new Color(1, 1, 1, a);
            });
            return Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }
        static Sprite StarFull => starFull != null ? starFull : starFull = StarSprite(true);
        static Sprite StarLine => starLine != null ? starLine : starLine = StarSprite(false);

        void BuildSelect()
        {
            var safe = Holo.SafeRoot(selectRt);
            gradL ??= Grad(true); gradB ??= Grad(false);
            var ring = ProcTex.Circle(128, 4);

            // ---- the planet: a soft glow, a thin orbit, the rendered sphere, the zone markers
            planetArea = R(safe, "Planet", new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(666, -398), new Vector2(830, 830));
            Holo.Img(Holo.Stretch(Holo.Rect(planetArea, "Glow"), -120, -120, -120, -120), Holo.GlowSprite, Holo.A(Holo.Cyan, 0.22f));
            var orbit = Holo.Rect(planetArea, "Orbit", new Vector2(0.5f, 0.5f), new Vector2(-10, -20), new Vector2(900, 400)); Holo.Img(orbit, ProcTex.Circle(256, 2), Holo.A(Hex("#7fc8f0"), 0.35f));
            orbit.localRotation = Quaternion.Euler(0, 0, 18);
            planetRaw = Holo.Raw(Holo.Fill(planetArea, "Sphere"), null, Color.white);
            planetRaw.raycastTarget = true;
            var drag = planetRaw.gameObject.AddComponent<DragArea>();
            drag.onDrag = d => { var r = Quaternion.AngleAxis(-d.x * 0.25f, Vector3.up) * Quaternion.AngleAxis(-d.y * 0.25f, Vector3.right); q = r * q; focusing = false; idleT = 0; };
            for (int i = 0; i < Sectors.Length; i++)
            {
                int k = i;
                var p = Holo.Rect(planetArea, "Pin" + i, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));
                var cg = p.gameObject.AddComponent<CanvasGroup>();
                var ret = Holo.Img(R(p, "Reticle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 120)), ProcTex.Circle(256, 2), Holo.A(BLime, 0.75f));
                var disc = Holo.Img(R(p, "Disc", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48, 48)), Holo.DotSprite, new Color(0.02f, 0.07f, 0.11f, 0.82f));
                var rg = Holo.Img(R(p, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52, 52)), ring, Holo.A(Holo.Cyan, 0.9f));
                var dot = Holo.Img(R(p, "Dot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26)), Holo.DotSprite, Holo.Cyan, false, true);
                var lk = Holo.Img(R(p, "Lock", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24)), Holo.Icon("lock"), BInk);
                var dia = Holo.Img(R(p, "Diamond", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -36), new Vector2(7, 7)), Holo.WhiteSprite, BInk); dia.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
                disc.raycastTarget = true; rg.raycastTarget = true;
                p.gameObject.AddComponent<Tap>().onTap = () => Pick(k, true);
                pins.Add((p, dot, rg, lk, cg, disc, dia, ret));
                var nt = Holo.Img(R(p, "New", new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-8, -8), new Vector2(14, 14)), Holo.DotSprite, BGold); nt.raycastTarget = false; newTags.Add(nt);
            }
            pinLabel = R(planetArea, "PinLabel", new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 44));
            Frame(pinLabel, new Color(0.01f, 0.03f, 0.05f, 0.88f), Holo.A(BSteel, 0.9f), 2.4f);
            pinLabelT = T(pinLabel, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 40), "", 22, BInk, TextAnchor.MiddleCenter, true);

            // ---- header: the title, the star track, ROUTE / BACK, the TEST chip
            var tl = T(safe, "Title", new Vector2(0, 1), new Vector2(0, 1), new Vector2(152, -22), new Vector2(800, 84), "SELECT DROP ZONE", 66, BInk, TextAnchor.MiddleLeft, true);
            tl.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.5f);
            subtitle = T(safe, "Sub", new Vector2(0, 1), new Vector2(0, 1), new Vector2(156, -94), new Vector2(900, 28), "", 21, Hex("#a9c6d8"), TextAnchor.MiddleLeft);
            testBadge = R(safe, "TestBadge", new Vector2(0, 1), new Vector2(0, 1), new Vector2(154, -128), new Vector2(250, 42));
            Frame(testBadge, new Color(0.17f, 0.11f, 0.02f, 0.92f), Hex("#ffb826"), 2.4f, true);
            testBadgeT = T(testBadge, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250, 36), "", 20, Hex("#ffb826"), TextAnchor.MiddleCenter, true);
            Press(testBadge, () => { int max = Sectors[Selected].def != null && Sectors[Selected].def.endless ? 26 : 5, st = Sectors[Selected].def != null && Sectors[Selected].def.endless ? 5 : 1; int f = Prefs.TestFloor + st; if (f > max) f = 1; Prefs.TestFloor = f; RefreshTestBadge(); });
            var route = R(safe, "Route", new Vector2(0, 1), new Vector2(0, 1), new Vector2(810, -41), new Vector2(195, 59));
            Frame(route, new Color(0.02f, 0.07f, 0.11f, 0.9f), Hex("#4a6a84"), 2f, true);
            Holo.Img(R(route, "Ic", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(30, 30)), Holo.Icon("squad"), BInk);
            T(route, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(86, 0), new Vector2(110, 40), "ROUTE", 26, BInk, TextAnchor.MiddleLeft, true);
            Press(route, () => { routeT = 2.4f; Sfx.Deny(); });
            var back = R(safe, "Back", new Vector2(0, 1), new Vector2(0, 1), new Vector2(1029, -41), new Vector2(195, 59));
            Frame(back, new Color(0.02f, 0.07f, 0.11f, 0.9f), Hex("#4a6a84"), 2f, true);
            Holo.Img(R(back, "Ic", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(28, 28)), Holo.Icon("back"), BInk);
            T(back, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(96, 0), new Vector2(100, 40), "BACK", 26, BInk, TextAnchor.MiddleLeft, true);
            Press(back, Back);
            routeToast = T(safe, "RouteToast", new Vector2(0, 1), new Vector2(0, 1), new Vector2(810, -108), new Vector2(560, 34), "CAMPAIGN ROUTE MAP  //  COMING SOON", 22, Holo.Acid, TextAnchor.MiddleLeft, true);

            // ---- the destination cards (drag to browse; picking one keeps it in view)
            var view = Holo.Rect(safe, "CardsView"); view.anchorMin = Vector2.zero; view.anchorMax = new Vector2(1, 0); view.pivot = Vector2.zero;
            view.offsetMin = new Vector2(34, 54); view.offsetMax = new Vector2(-690, 54 + CardH + 44);
            view.gameObject.AddComponent<RectMask2D>();
            Holo.Img(view, Holo.WhiteSprite, new Color(0, 0, 0, 0.001f), false, true);   // catches drags between cards
            var row = Holo.Rect(view, "Cards", new Vector2(0, 0), Vector2.zero, new Vector2(Sectors.Length * CardStep + 36, CardH + 44)); row.pivot = Vector2.zero;
            cardStrip = view.gameObject.AddComponent<ScrollRect>(); cardStrip.content = row; cardStrip.viewport = view; cardStrip.horizontal = true; cardStrip.vertical = false;
            cardStrip.movementType = ScrollRect.MovementType.Clamped; cardStrip.inertia = true; cardStrip.decelerationRate = 0.12f;
            for (int i = 0; i < Sectors.Length; i++)
            {
                int k = i; var s = Sectors[i];
                var c = R(row, "Card" + i, Vector2.zero, Vector2.zero, new Vector2(18 + i * CardStep, 22), new Vector2(CardW, CardH));
                var glow = Holo.Img(Holo.Stretch(Holo.Rect(c, "Glow"), -30, -30, -30, -30), Holo.GlowSprite, Holo.A(Hex("#6ba7de"), 0));
                var (fill, _) = (Holo.Img(Holo.Fill(c, "Glass"), Holo.FillSprite, new Color(0.02f, 0.07f, 0.11f, 0.96f), true, true), 0);
                fill.pixelsPerUnitMultiplier = 2f;
                var artRt = Holo.Stretch(Holo.Rect(c, "Art"), 6, 78, 6, 6); artRt.gameObject.AddComponent<RectMask2D>();
                var tex = Resources.Load<Texture2D>("UI/Sectors/" + s.art);
                var art = Holo.Raw(Holo.Fill(artRt, "Img"), tex, Color.white); var cu = art.gameObject.AddComponent<CoverUv>(); if (tex != null) cu.aspect = (float)tex.width / tex.height;
                var shade = Holo.Rect(artRt, "Shade"); shade.anchorMin = Vector2.zero; shade.anchorMax = new Vector2(1, 0); shade.pivot = new Vector2(0.5f, 0); shade.sizeDelta = new Vector2(0, 40);
                Holo.Raw(shade, gradB, new Color(0.02f, 0.07f, 0.11f, 0.9f));
                var edge = Holo.Img(Holo.Fill(c, "Edge"), Holo.EdgeSprite, BSteel, true); edge.pixelsPerUnitMultiplier = 2f;
                var nm = T(c, "Name", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 54), new Vector2(CardW - 8, 30), s.name, 24, Hex("#e6eef4"), TextAnchor.MiddleCenter, true);
                nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 14; nm.resizeTextMaxSize = 24; nm.horizontalOverflow = HorizontalWrapMode.Wrap;
                var st = new Image[3];
                for (int k2 = 0; k2 < 3; k2++) st[k2] = Holo.Img(R(c, "Star" + k2, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2((k2 - 1) * 32, 22), new Vector2(24, 24)), StarLine, Holo.A(BMuted, 0.8f));
                var lb = R(c, "LockBadge", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(46, 46));
                Holo.Img(lb, Holo.DotSprite, new Color(0.01f, 0.03f, 0.05f, 0.85f));
                Holo.Img(R(lb, "Ic", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24)), Holo.Icon("lock"), BInk);
                c.gameObject.AddComponent<Tap>().onTap = () => Pick(k, true);
                cards.Add((c, glow, edge, art, st, lb));
            }

            // ---- the picked zone (right)
            var side = Holo.Rect(safe, "Side"); side.anchorMin = new Vector2(1, 0); side.anchorMax = new Vector2(1, 1); side.pivot = new Vector2(1, 0.5f);
            side.offsetMin = new Vector2(-656, 72); side.offsetMax = new Vector2(-33, -76);
            Frame(side, new Color(0.016f, 0.07f, 0.12f, 0.93f), BSteel);
            secName = T(side, "Name", new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -24), new Vector2(560, 76), "", 66, BInk, TextAnchor.MiddleLeft, true);
            var artFrame = Holo.Rect(side, "Art"); artFrame.anchorMin = Vector2.zero; artFrame.anchorMax = Vector2.one; artFrame.offsetMin = new Vector2(34, 521); artFrame.offsetMax = new Vector2(-27, -102);
            artFrame.gameObject.AddComponent<RectMask2D>();
            secArt = Holo.Raw(Holo.Fill(artFrame, "Img"), null, Color.white); secArt.gameObject.AddComponent<CoverUv>();
            Holo.Img(Holo.Fill(artFrame, "Edge"), Holo.EdgeSprite, Holo.A(BSteel, 0.9f), true).pixelsPerUnitMultiplier = 3f;
            var rows = R(side, "Rows", new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 336), new Vector2(0, 185)); rows.anchorMax = new Vector2(1, 0); rows.sizeDelta = new Vector2(-61, 185);
            Holo.Img(rows, Holo.FillSprite, new Color(0.01f, 0.05f, 0.09f, 0.6f), true).pixelsPerUnitMultiplier = 3f;
            Holo.Img(Holo.Fill(rows, "Edge"), Holo.EdgeSprite, Holo.A(BSteel, 0.55f), true).pixelsPerUnitMultiplier = 3f;
            string[] rl = { "RECOMMENDED POWER", "BEST ATTEMPT", "THREAT" };
            for (int i = 0; i < 3; i++)
            {
                float y = 185 - 33 - i * 59;
                T(rows, "L" + i, Vector2.zero, new Vector2(0, 0.5f), new Vector2(26, y), new Vector2(240, 30), Track(rl[i], 6), 21, Hex("#a9c6d8"), TextAnchor.MiddleLeft);
                if (i < 2) { var dv = R(rows, "Div" + i, Vector2.zero, new Vector2(0, 0.5f), new Vector2(14, y - 30), new Vector2(0, 2)); dv.anchorMax = new Vector2(1, 0); dv.sizeDelta = new Vector2(-28, 2); Holo.Img(dv, Holo.WhiteSprite, Holo.A(BSteel, 0.6f)); }
            }
            var bars = R(rows, "Bars", Vector2.zero, new Vector2(0, 0.5f), new Vector2(276, 185 - 33), new Vector2(34, 30));
            for (int j = 0; j < 3; j++) Holo.Img(R(bars, "B" + j, Vector2.zero, Vector2.zero, new Vector2(j * 11, 2), new Vector2(8, 12 + j * 7)), Holo.WhiteSprite, Hex("#cfe9f5"));
            dPower = T(rows, "Power", Vector2.zero, new Vector2(0, 0.5f), new Vector2(318, 185 - 33), new Vector2(220, 40), "", 34, Color.white, TextAnchor.MiddleLeft, true);
            for (int j = 0; j < 3; j++) dBestStars[j] = Holo.Img(R(rows, "BS" + j, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(276 + 16 + j * 42, 185 - 92), new Vector2(30, 30)), StarLine, Holo.A(BMuted, 0.8f));
            dBest = T(rows, "Best", Vector2.zero, new Vector2(0, 0.5f), new Vector2(276 + 136, 185 - 92), new Vector2(200, 34), "", 26, BInk, TextAnchor.MiddleLeft, true);
            for (int j = 0; j < 5; j++) threatPips.Add(Holo.Img(R(rows, "Pip" + j, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(276 + 14 + j * 42, 185 - 151), new Vector2(32, 32)), Holo.Icon("bolt"), BRed));
            dBlurb = T(side, "Blurb", Vector2.zero, new Vector2(0, 0.5f), new Vector2(38, 304), new Vector2(560, 50), "", 22, Hex("#d0dbe3"), TextAnchor.MiddleLeft);
            dBlurb.horizontalOverflow = HorizontalWrapMode.Wrap; dBlurb.resizeTextForBestFit = true; dBlurb.resizeTextMinSize = 15; dBlurb.resizeTextMaxSize = 22;
            var dv2 = R(side, "Div", Vector2.zero, new Vector2(0, 0.5f), new Vector2(38, 264), new Vector2(0, 2)); dv2.anchorMax = new Vector2(1, 0); dv2.sizeDelta = new Vector2(-72, 2);
            Holo.Raw(dv2, gradL, Holo.A(BSteel, 0.9f));
            T(side, "WeeklyL", Vector2.zero, new Vector2(0, 0.5f), new Vector2(42, 236), new Vector2(400, 30), Track("WEEKLY CHALLENGE", 10), 21, Hex("#a9c6d8"), TextAnchor.MiddleLeft);
            var info = R(side, "WeeklyInfo", new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-64, 236), new Vector2(46, 46));
            Holo.Img(info, ProcTex.Circle(128, 6), Hex("#cfe9f5"), false, true);
            T(info, "i", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40), "i", 28, Hex("#cfe9f5"), TextAnchor.MiddleCenter, true);
            Press(info, () => dWeeklyTipRt.gameObject.SetActive(!dWeeklyTipRt.gameObject.activeSelf));
            dWeeklyChip = R(side, "WeeklyChip", Vector2.zero, new Vector2(0, 0.5f), new Vector2(42, 186), new Vector2(357, 48));
            (dWeeklyFill, dWeeklyEdge) = Frame(dWeeklyChip, new Color(0.17f, 0.085f, 0.03f, 0.92f), BOrange, 2.4f);
            dWeeklyIc = Holo.Img(R(dWeeklyChip, "Ic", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(32, 32)), Holo.DotSprite, Hex("#f5a623"));
            dWeeklyGlyph = Holo.Img(R((RectTransform)dWeeklyIc.transform, "G", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22)), Holo.Icon("bolt"), Hex("#2a1405"));
            dWeekly = T(dWeeklyChip, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(58, 0), new Vector2(600, 40), "", 24, Hex("#feb637"), TextAnchor.MiddleLeft, true);
            // BRIEFING (or, locked, why)
            Holo.Img(R(side, "BriefGlow", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(3, -6), new Vector2(620, 170)), Holo.GlowSprite, Holo.A(BLime, 0.3f));
            deployBtn = R(side, "Deploy", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(3, 26), new Vector2(557, 106));
            Frame(deployBtn, Hex("#cee74e"), Hex("#e4ff7a"), 1.3f, true);
            var doc = R(deployBtn, "Doc", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(138, 0), new Vector2(38, 50));
            Holo.Img(doc, Holo.FillSprite, Hex("#091904"), true).pixelsPerUnitMultiplier = 4f;
            for (int j = 0; j < 4; j++) Holo.Img(R(doc, "L" + j, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -14 - j * 8), new Vector2(22, 3)), Holo.WhiteSprite, Hex("#cee74e"));
            T(deployBtn, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(182, 0), new Vector2(300, 80), "BRIEFING", 56, Hex("#091904"), TextAnchor.MiddleLeft, true);
            Holo.Img(R(deployBtn, "Chev", new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-62, 0), new Vector2(46, 46)), Holo.Icon("chevrons"), Hex("#091904"));
            Press(deployBtn, OpenBrief);
            lockedBox = R(side, "Locked", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(3, 26), new Vector2(557, 106));
            Frame(lockedBox, new Color(0.03f, 0.06f, 0.09f, 0.95f), Holo.A(Holo.Red, 0.85f), 1.3f);
            Holo.Img(R(lockedBox, "Ic", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60, 0), new Vector2(44, 44)), Holo.Icon("lock"), Holo.Red);
            lockLine = T(lockedBox, "T", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(106, 0), new Vector2(430, 80), "", 26, BInk, TextAnchor.MiddleLeft, true);
            lockLine.horizontalOverflow = HorizontalWrapMode.Wrap; lockLine.resizeTextForBestFit = true; lockLine.resizeTextMinSize = 16; lockLine.resizeTextMaxSize = 26;
            // the weekly (i): each modifier's own effect (the briefing's copy)
            dWeeklyTipRt = R(side, "WeeklyTip", Vector2.zero, new Vector2(0, 0), new Vector2(30, 262), new Vector2(0, 150)); dWeeklyTipRt.anchorMax = new Vector2(1, 0); dWeeklyTipRt.sizeDelta = new Vector2(-60, 150);
            Frame(dWeeklyTipRt, new Color(0.01f, 0.03f, 0.05f, 0.97f), Holo.A(BOrange, 0.9f), 2f, true);
            dWeeklyTip = T(dWeeklyTipRt, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 130), "", 21, Hex("#e6eef4"), TextAnchor.MiddleLeft);
            dWeeklyTip.horizontalOverflow = HorizontalWrapMode.Wrap;
            Press(dWeeklyTipRt, () => dWeeklyTipRt.gameObject.SetActive(false));
            dWeeklyTipRt.gameObject.SetActive(false);
        }

        void RefreshTestBadge()
        {
            if (testBadge == null) return; testBadge.gameObject.SetActive(Prefs.TestMode);
            testBadgeT.text = $"TEST  ·  FLOOR {Prefs.TestFloor}{(Prefs.TestPower ? "  ·  RP" : "")}";
        }
        // the star track line: sectors, rank, stars
        void RefreshStarTrack()
        {
            if (subtitle == null) return;
            subtitle.text = $"{Track($"{SectorDefs.All.Count} SECTORS", 7)}<size=19>   •   </size>{Track($"RANK {Meta.Rank}", 7)}<size=19>   •   </size>{Track($"STARS {Meta.StarsTotal()}/162", 7)}";
        }

        // the picked zone's values only (no rebuild)
        void RefreshPanel()
        {
            var s = Sectors[Selected];
            secName.text = s.name; secName.fontSize = 66; float pw = secName.preferredWidth; if (pw > 560) secName.fontSize = Mathf.FloorToInt(66 * 560 / pw);
            var tex = Resources.Load<Texture2D>("UI/Sectors/" + s.art);
            secArt.texture = tex; if (tex != null) secArt.GetComponent<CoverUv>().aspect = (float)tex.width / tex.height;
            dPower.text = s.Rec.ToString("N0");
            // best attempt: the zone's stars and its best grade; nothing yet = an empty state
            var def = s.def; int stars = 0; string grade = null;
            if (def != null)
            {
                for (int k = 0; k < 3; k++) { bool has = Progress.HasStar(def, k); if (has) stars++; dBestStars[k].sprite = has ? StarFull : StarLine; dBestStars[k].color = has ? BGold : Holo.A(BMuted, 0.8f); }
                int g = PlayerPrefs.GetInt("ms.grade." + def.id, -1); if (g >= 0) grade = "CBAS"[g].ToString();
            }
            else foreach (var st in dBestStars) st.color = Holo.A(BMuted, 0.7f);
            bool attempted = stars > 0 || grade != null || def != null && Progress.Cleared(def);
            foreach (var st in dBestStars) st.gameObject.SetActive(attempted && def != null);
            dBest.rectTransform.anchoredPosition = new Vector2(attempted && def != null ? 412 : 276, dBest.rectTransform.anchoredPosition.y);
            dBest.text = !attempted ? "<color=#7c9ebb>NO ATTEMPT YET</color>" : grade != null ? $"GRADE {grade}" : "—";
            for (int i = 0; i < threatPips.Count; i++) threatPips[i].color = i < s.threat ? BRed : Hex("#3a5468");
            dBlurb.text = def != null ? Sentence(def.blurb) : "The escape test map: clear each room to open the next door.";
            RefreshWeekly();
            deployBtn.gameObject.SetActive(!s.locked);
            lockedBox.gameObject.SetActive(s.locked);
            lockLine.text = s.locked ? s.unlock : "";
        }
        // the weekly challenge: shown from Commander Rank 8 (or the dev override); not in Swarm Gate (endless runs skip it)
        void RefreshWeekly()
        {
            var def = Sectors[Selected].def; bool on = Ops.WeeklyOn, endless = def != null && def.endless;
            bool live = on && !endless;
            dWeeklyFill.color = live ? new Color(0.17f, 0.085f, 0.03f, 0.92f) : new Color(0.03f, 0.06f, 0.09f, 0.92f);
            dWeeklyEdge.color = live ? BOrange : Holo.A(BSteel, 0.9f);
            dWeeklyIc.sprite = live ? Holo.DotSprite : Holo.Icon("lock"); dWeeklyIc.color = live ? Hex("#f5a623") : BSoft; dWeeklyGlyph.enabled = live;
            dWeekly.color = live ? Hex("#feb637") : BSoft;
            if (on) { var (a, b) = Ops.WeeklyPair; dWeekly.text = endless ? "NOT IN SWARM GATE" : $"{ZoneMods.Names[a]} + {ZoneMods.Names[b]}"; dWeeklyTip.text = $"<b>{ZoneMods.Names[a]}:</b> {Lower(ModDesc[a])}\n<b>{ZoneMods.Names[b]}:</b> {Lower(ModDesc[b])}\n<color=#feb637>+25% CREDITS</color>" + (endless ? "\n<color=#7c9ebb>Swarm Gate runs without it.</color>" : ""); }
            else { dWeekly.text = "UNLOCKS AT COMMANDER RANK 8"; dWeeklyTip.text = "A weekly pair of modifiers on every zone, for +25% credits. Unlocks at Commander Rank 8."; }
            dWeeklyChip.sizeDelta = new Vector2(Mathf.Min(540, dWeekly.preferredWidth + 82), 48);
        }
        static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        void TickSelect(float dt)
        {
            if (focusing) { q = Quaternion.Slerp(q, qGoal, 1 - Mathf.Exp(-dt * 4)); if (Quaternion.Angle(q, qGoal) < 0.2f) focusing = false; }
            else if (idleT > 5) q = Quaternion.AngleAxis(-4f * dt, q * Vector3.up) * q; // slow drift when left alone
            planet.localRotation = q;
            if (infestMat != null) infestMat.SetFloat("_Cutoff", Mathf.Lerp(0.3f, 0.06f, Mathf.SmoothStep(0, 1, t / 90f)) + 0.02f * Mathf.Sin(t * 1.3f));
            var area = planetArea.rect; bool labelOn = false;
            for (int i = 0; i < pins.Count; i++)
            {
                var s = Sectors[i]; var p = pins[i];
                var d = q * DirDeg(s.lat, s.lon);
                var vp = planetCam.WorldToViewportPoint(RigAt + d * 1.0f);
                p.rt.anchoredPosition = new Vector2((vp.x - 0.5f) * area.width, (vp.y - 0.5f) * area.height);
                float facing = Vector3.Dot(d, Vector3.back);
                p.cg.alpha = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.05f, 0.35f, facing));
                p.cg.blocksRaycasts = facing > 0.2f;
                bool sel = i == Selected, hot = s.def != null && s.def.id == hotId, locked = s.locked;
                float pulse = sel ? 1 + 0.06f * Mathf.Sin(t * 4) : 1;
                p.rt.localScale = Vector3.one * pulse;
                var col = sel ? BLime : hot ? new Color(1f, 0.45f, 0.1f) : s.def != null && Progress.Cleared(s.def) ? BGold : s.def != null ? s.def.accent : Holo.Cyan;
                p.reticle.enabled = sel; p.reticle.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 20);
                p.disc.enabled = !sel; p.diamond.enabled = !sel;
                p.ring.color = locked && !sel ? Holo.A(BInk, 0.55f) : Holo.A(col, sel ? 1 : 0.85f);
                p.ring.rectTransform.sizeDelta = Vector2.one * (sel ? 62 : 52);
                p.dot.enabled = !locked || sel; p.dot.color = col; p.dot.rectTransform.sizeDelta = Vector2.one * (sel ? 30 : 22);
                p.lockIc.enabled = locked && !sel;
                if (sel && facing > 0.2f) { labelOn = true; pinLabel.anchoredPosition = p.rt.anchoredPosition + new Vector2(48, 0); }
                if (i < newTags.Count) { bool nw = !locked && s.def != null && !s.def.endless && !Progress.Cleared(s.def) && !seenCache.Contains(s.def.id) && !sel; if (newTags[i].enabled != nw) newTags[i].enabled = nw; }
            }
            if (pinLabel.gameObject.activeSelf != labelOn) pinLabel.gameObject.SetActive(labelOn);
            if (labelOn && pinLabelT.text != Sectors[Selected].name) { pinLabelT.text = Sectors[Selected].name; pinLabel.sizeDelta = new Vector2(pinLabelT.preferredWidth + 34, 44); }
            pinLabel.SetAsLastSibling();
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i]; bool sel = i == Selected; var def = Sectors[i].def;
                c.rt.localScale = Vector3.one * Mathf.Lerp(c.rt.localScale.x, sel ? 1.03f : 1f, dt * 10);
                c.edge.color = sel ? Hex("#7fc4f2") : BSteel;
                c.glow.color = Holo.A(Hex("#6ba7de"), Mathf.Lerp(c.glow.color.a, sel ? 0.45f : 0f, dt * 8));
                for (int k2 = 0; k2 < 3; k2++) { bool has = def != null && Progress.HasStar(def, k2); c.stars[k2].sprite = has ? StarFull : StarLine; c.stars[k2].color = has ? BGold : Holo.A(BMuted, 0.8f); }
                bool lk = Sectors[i].locked; if (c.lockBadge.gameObject.activeSelf != lk) c.lockBadge.gameObject.SetActive(lk);
            }
            if (stripT > 0 && cardStrip != null) { stripT -= dt; cardStrip.horizontalNormalizedPosition = Mathf.Lerp(cardStrip.horizontalNormalizedPosition, stripTarget, 1 - Mathf.Exp(-dt * 10)); }
            routeT -= dt;
            routeToast.color = Holo.A(Holo.Acid, Mathf.Clamp01(routeT));
        }
    }
}
