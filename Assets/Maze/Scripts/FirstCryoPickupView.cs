using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

namespace Maze
{
    // Existing power-up lifetime, pickup radius and effects stay in PowerUps.
    // These first-floor display badges use the game's authored icon artwork.
    public sealed class FirstCryoPickupView : MonoBehaviour
    {
        static Mesh quad;
        readonly List<Material> owned = new();
        Transform badge;
        static readonly Dictionary<string, (string art, string label)> identities = new()
        {
            {"overcharge", ("card_firerate", "OVERCHARGE")}, {"cryo_rounds", ("card_range", "CRYO")},
            {"incendiary", ("evo_inferno", "INCENDIARY")}, {"bulwark", ("card_shield", "BULWARK")},
            {"med_gel", ("cache_med", "MED GEL")}, {"magnet", ("cur_biomass", "MAGNET")},
            {"orbital_strike", ("card_grenade", "ORBITAL")}, {"sentry_drone", ("card_marksman", "SENTRY")},
            {"adrenal", ("card_dashcd", "ADRENAL")}, {"tesla_coil", ("card_trail", "TESLA")},
            {"endless_mag", ("cache_supply", "AMMO")}, {"decoy", ("card_shove", "DECOY")},
            {"armour_breaker", ("card_pierce", "ARMOUR")}, {"stasis_field", ("card_shield", "STASIS")},
            {"purge_charge", ("card_grenade", "PURGE")}
        };
        public static Transform Build(Transform root, PowerUps.Def def)
        {
            var view = root.gameObject.AddComponent<FirstCryoPickupView>();
            view.badge = new GameObject("IdentityBadge").transform; view.badge.SetParent(root, false);
            view.badge.localPosition = new Vector3(0, 1.25f, 0);
            if (quad == null) { var primitive = GameObject.CreatePrimitive(PrimitiveType.Quad); quad = primitive.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(primitive); }
            var identity = identities.TryGetValue(def.id, out var found) ? found : ("ui_power", def.name);
            view.Panel("BadgeRim", view.badge, new Vector3(0,0,.035f), new Vector3(1.24f,1.24f,1), BlueUi.Blue, PolishGlyphs.Get("plate").texture);
            view.Panel("Backing", view.badge, new Vector3(0,0,.025f), new Vector3(1.16f,1.16f,1), new Color(.87f,.96f,1), PolishGlyphs.Get("plate").texture);
            var art = Holo.Art(identity.Item1);
            if (def.id == "cryo_rounds" || def.id == "stasis_field")
                view.Panel("FreezeGlyph",view.badge,Vector3.zero,new Vector3(.88f,.88f,1),BlueUi.Blue,PolishGlyphs.Get("freeze").texture);
            else view.Panel("Icon",view.badge,Vector3.zero,new Vector3(.84f,.84f,1),Color.white,art != null ? art.texture : Holo.Icon("bolt").texture);
            string title=System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(identity.Item2.ToLowerInvariant());
            float labelWidth=Mathf.Clamp(title.Length*.14f+.45f,1.50f,2.15f);
            view.Panel("NameRim",view.badge,new Vector3(0,-.68f,.03f),new Vector3(labelWidth+.06f,.56f,1),BlueUi.Blue,PolishGlyphs.Get("plate").texture);
            view.Panel("NamePlate",view.badge,new Vector3(0,-.68f,.02f),new Vector3(labelWidth,.50f,1),new Color(.87f,.96f,1),PolishGlyphs.Get("plate").texture);
            var textRoot=new GameObject("PickupName");textRoot.transform.SetParent(view.badge,false);textRoot.transform.localPosition=new Vector3(0,-.68f,0);
            var label=textRoot.AddComponent<TextMeshPro>();label.font=Resources.Load<TMP_FontAsset>("BlueUI/Fonts/Display");label.text=title;label.fontSize=2.9f;label.enableAutoSizing=false;label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Overflow;label.color=BlueUi.Navy;label.margin=Vector4.zero;
            label.rectTransform.sizeDelta=new Vector2(labelWidth-.12f,.42f);label.fontSharedMaterial=label.font.material;label.ForceMeshUpdate();
            var renderer=textRoot.GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var ring = new GameObject("PickupCollar"); ring.transform.SetParent(root, false); ring.transform.localPosition = new Vector3(0, .08f, 0); ring.transform.localScale = Vector3.one * .58f;
            ring.AddComponent<MeshFilter>().sharedMesh = Fx.Ring; var mr = ring.AddComponent<MeshRenderer>(); var material = Mats.Fade(new Color(.20f,.55f,.78f,.32f)); view.owned.Add(material); mr.sharedMaterial = material; mr.shadowCastingMode = ShadowCastingMode.Off;
            return view.badge;
        }
        void Panel(string name, Transform parent, Vector3 at, Vector3 size, Color color, Texture texture)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = at; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = quad; var material = Mats.Fade(color, texture); owned.Add(material);
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        void LateUpdate()
        {
            if (badge != null && Camera.main != null) badge.rotation = Camera.main.transform.rotation;
        }
        void OnDestroy() { foreach (var material in owned) if (material != null) Destroy(material); }
    }
}
