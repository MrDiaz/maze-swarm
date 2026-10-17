#if UNITY_EDITOR
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    public static partial class BlueUiProof
    {
        static bool richSound;
        static void AddRichHangarSteps(Vector2Int size)
        {
            Add(() => { MenuTitle(size); Wait(.7); });
            Add(() => { Click(Title.PlayButton); Wait(.8); });
            Add(() => { StaticHangarState(); CaptureScreen("rich_hangar"); });
            for (int n = 0; n < 3; n++)
            {
                int cycle = n;
                Add(() => { Click(P.HeroesButton); Wait(.7); });
                Add(() =>
                {
                    Check(P.Current == MenuPresentation.Screen.Heroes && P.SubscreenOpen, "Hangar Heroes route opens existing presentation, cycle " + cycle);
                    Check(Meta.Heroes.SequenceEqual(new[] { "vanguard" }) && Meta.HeroId == "vanguard" && Prefs.Vanguard, "Heroes route retains one shared Heavy progression and gameplay baseline, cycle " + cycle);
                    Check(P.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(x => x.name.StartsWith("Hero_")).Select(x => x.name).OrderBy(x => x).SequenceEqual(new[] { "Hero_engineer", "Hero_vanguard" }), "Heroes route presents both Heavy and Engineer cards, cycle " + cycle);
                    if (cycle == 0) CaptureScreen("rich_hangar_heroes");
                    Click(Button(P, "Back")); Wait(.7);
                });
                Add(() => { Check(P.Current == MenuPresentation.Screen.Home && P.Visible, "Heroes Back returns to Hangar, cycle " + cycle); Click(P.GearButton); Wait(.8); });
                Add(() =>
                {
                    Check(Gear.Visible, "Hangar Gear opens retained Equipment, cycle " + cycle);
                    Check(Object.FindObjectsByType<LivingEnvironmentView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(v => v.IsArmory && v.Rendering), "Equipment keeps its live workshop, cycle " + cycle);
                    if (cycle == 0) CaptureScreen("rich_hangar_equipment");
                    CloseGear(); Wait(.8);
                });
                Add(() => { Check(P.Visible && P.Current == MenuPresentation.Screen.Home, "Equipment Back returns to Hangar, cycle " + cycle); Click(P.SettingsButton); Wait(.8); });
                Add(() =>
                {
                    var sheet = Field<object>(G.menu, "current");
                    Check(G.menu.LegacySheetOpen && Field<string>(sheet, "key") == "settings", "Hangar Settings opens the actual implemented sheet, cycle " + cycle);
                    richSound = Prefs.Sound;
                    var sound = G.menu.GetComponentsInChildren<HoloButton>(false).First(x => x.name == "Row_SOUND");
                    sound.onClick.Invoke(); Check(Prefs.Sound != richSound, "Settings uses the actual Sound preference, cycle " + cycle);
                    sound.onClick.Invoke(); Check(Prefs.Sound == richSound, "Sound preference restored by the real callback, cycle " + cycle);
                    if (cycle == 0) CaptureScreen("rich_hangar_settings");
                    G.menu.CloseSheet(); Wait(.8);
                });
                Add(() => { StaticHangarState(); Check(Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length == 1, "repeated Hangar navigation retains one EventSystem, cycle " + cycle); });
            }
            Add(() => { P.Back(); Wait(.7); });
            Add(() => { Check(Title.Visible && Title.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(Visible).All(x => x == Title.PlayButton), "returning to title preserves Play-only title navigation"); Click(Title.PlayButton); Wait(.8); });
            Add(() => { StaticHangarState(); Progress.D.runs = 0; Click(P.HeroesButton); Wait(.3); });
            Add(() => { Check(P.Current == MenuPresentation.Screen.Home, "Heroes retains the existing first-drop unlock gate"); Click(P.GearButton); Wait(.3); });
            Add(() => { Check(!Gear.Visible, "Gear retains the existing first-drop unlock gate"); Fixture(); Wait(.4); });
        }
    }
}
#endif
