using UnityEngine;

namespace Maze
{
    // A serialised component needs a matching script filename so Unity can retain its reference.
    // Each authored Cryo room owns its contact texture and the footprint covered by that texture.
    public sealed class CryoSoftRoomData : MonoBehaviour
    {
        public Texture2D ao;
        public Vector2 size = new Vector2(44, 32);
    }
}
