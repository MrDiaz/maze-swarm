using UnityEngine;

namespace Maze
{
    // Display resources for the selected garage/cargo direction. Sector IDs and save keys stay authoritative.
    public static class CohesiveMenuArt
    {
        public const string HangarResource = "CohesiveZone/Art/hangar-purple";
        public const string CargoResource = "CohesiveZone/Art/cargo-preview";
        public static Texture2D Hangar => Resources.Load<Texture2D>(HangarResource);
        public static Texture2D Cargo => Resources.Load<Texture2D>(CargoResource);
        static Sprite cargoSprite;
        public static Sprite CargoSprite
        {
            get
            {
                if (cargoSprite != null) return cargoSprite;
                // Prefer a clean, actual runtime capture when the integration proof provides one.
                var actual = Resources.Load<Texture2D>("CohesiveZone/Art/cargo-arena");
                var texture = actual != null ? actual : Cargo;
                if (texture == null) return null;
                var rect = actual != null ? new Rect(0,0,texture.width,texture.height) :
                    new Rect(texture.width*.19f,texture.height*.20f,texture.width*.59f,texture.height*.66f);
                // The selected concept's corner HUD is excluded from the illustration crop.
                cargoSprite = Sprite.Create(texture, rect, Vector2.one*.5f, 100, 0, SpriteMeshType.FullRect);
                cargoSprite.name = actual != null ? "CargoDepotRuntimePreview" : "CargoDepotConceptPreview";
                return cargoSprite;
            }
        }
        public static readonly Vector2 HeroFeet = new Vector2(.52f, .81f);
        public const float HeroVisibleHeight = .43f;

        public static void Background(Transform parent)
        {
            var texture = Hangar;
            var raw = Holo.Raw(Holo.Fill(parent, "PurpleGarageEnvironment"), texture, Color.white);
            raw.raycastTarget = true;
            if (texture != null) raw.gameObject.AddComponent<CoverUv>().aspect = (float)texture.width / texture.height;
            var shade = Holo.Img(Holo.Fill(parent, "PurpleGarageShade"), Holo.WhiteSprite, new Color(.09f, .07f, .20f, .16f));
            shade.raycastTarget = false;
        }

        public static string SectorName(SectorDef sector, string fallback = "Run") =>
            sector == null ? fallback : CohesiveZone.For(sector) ? BlueRunPresentation.TitleCase(CohesiveZone.Title(sector)) : BlueRunPresentation.TitleCase(sector.name);

        public static string Text(string value)
        {
            if (!CohesiveZone.Enabled || string.IsNullOrEmpty(value)) return value;
            return value.Replace("CRYO STATION", "CARGO DEPOT").Replace("Cryo Station", "Cargo Depot");
        }

        public static string FloorName(int floor)
        {
            switch (floor)
            {
                case 1: return "Cargo Yard";
                case 2: return "Loading Bays";
                case 3: return "Power Depot";
                case 4: return "Freight Lanes";
                case 5: return "Command Dock";
                default: return "Floor " + floor;
            }
        }

        // Keep the hero and navigation registered to the same safe-area design frame on wide phones.
        // Background art may crop vertically to cover the device; the live hero must not slide under Deploy.
        static UnityEngine.UI.RawImage registeredPlate;
        static RectTransform registeredFrame;
        public static void PlaceHero(RectTransform target, UnityEngine.UI.RawImage plate, Vector2 designUv, Vector2 heightUnits)
        {
            if (target == null || plate == null) return;
            if (registeredPlate != plate || registeredFrame == null)
            {
                registeredPlate = plate; registeredFrame = null;
                foreach (var candidate in plate.transform.parent.GetComponentsInChildren<RectTransform>(true))
                    if (candidate.name == "HangarLayout") { registeredFrame = candidate; break; }
            }
            var frame = registeredFrame;
            if (frame == null) return;
            var local = new Vector3(frame.rect.xMin + designUv.x * frame.rect.width,
                frame.rect.yMax - designUv.y * frame.rect.height, 0);
            target.anchorMin = target.anchorMax = target.pivot = Vector2.one * .5f;
            target.position = frame.TransformPoint(local);
            var parent = (RectTransform)target.parent;
            float scale = parent.InverseTransformVector(frame.TransformVector(Vector3.up)).magnitude;
            target.sizeDelta = heightUnits * frame.rect.height * scale;
        }
    }
}
