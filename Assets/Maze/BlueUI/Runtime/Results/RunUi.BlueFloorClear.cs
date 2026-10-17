using UnityEngine;

namespace Maze
{
    public partial class RunUi
    {
        BlueFloorClearView blueFloorClear;
        int lastClearedShown;

        // Called after ShowChoice has populated its existing generated offer.
        void InterceptBlueFloorClear(Game.Choice choice)
        {
            if (choice != Game.Choice.Cache || lastClearedShown == game.floor || Game.SimRun || game.demo) return;
            lastClearedShown = game.floor;
            if (blueFloorClear == null)
            {
                var root = Holo.Fill(canvas.transform, "FloorCleared");
                blueFloorClear = root.gameObject.AddComponent<BlueFloorClearView>();
                blueFloorClear.Init(game);
            }
            choiceRt.gameObject.SetActive(false);
            blueFloorClear.Show(() =>
            {
                if (game.state == Game.State.Choosing && game.choosing == Game.Choice.Cache) choiceRt.gameObject.SetActive(true);
            });
        }

        void HideBlueFloorClear() { if (blueFloorClear != null) blueFloorClear.gameObject.SetActive(false); }
        void ResetBlueFloorClear() { lastClearedShown = 0; HideBlueFloorClear(); }
    }
}
