using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // (2026-10-15, Manuel: "you hide them") The menu's 3D backdrops (LivingEnvironments: the Armory workshop / command ship
    // worlds and their hero previews) are root objects that stay live behind a run, ~250k triangles in the shadow pass.
    // While a run is in progress their worlds are switched off; back at the menu exactly the ones this switched off come
    // back. Nothing under Assets/Maze/LivingEnvironments is touched: only each view's public World object.
    public class MenuSceneryGate : MonoBehaviour
    {
        public static int Hidden;   // (checks) worlds hidden right now
        readonly List<GameObject> off = new(); Game game; bool inRun;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var go = new GameObject("MenuSceneryGate"); DontDestroyOnLoad(go); go.AddComponent<MenuSceneryGate>(); }

        void LateUpdate()
        {
            if (game == null) { game = FindAnyObjectByType<Game>(); if (game == null) return; }
            bool run = game.run && game.state != Game.State.Menu;
            if (run == inRun) return;
            inRun = run;
            if (run)
            {
                foreach (var v in FindObjectsByType<LivingEnvironmentView>(FindObjectsInactive.Include))
                    if (v != null && v.World != null && v.World.gameObject.activeSelf) { v.World.gameObject.SetActive(false); off.Add(v.World.gameObject); }
            }
            else
            {
                foreach (var w in off) if (w != null) w.SetActive(true);
                off.Clear();
            }
            Hidden = off.Count;
        }
    }
}
