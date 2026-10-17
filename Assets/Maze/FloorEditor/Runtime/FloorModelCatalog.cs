using System;
using UnityEngine;

namespace Maze.FloorEditing
{
    [Serializable] public sealed class FloorModelDefinition
    {
        public string id, slug, label, category, resourcePath, selectedVersion;
        public bool unrigged;
        public float sizeX, sizeY, sizeZ, centerY;
        public int mobileFirstLevel;
        public int[] sourceLevels, triangles;
    }
    public static class FloorModelCatalog
    {
        [Serializable] public sealed class Catalog { public FloorModelDefinition[] entries; }
        static FloorModelDefinition[] entries;
        public static bool IsLocalId(string id) => id != null && id.StartsWith("local-20261016-", StringComparison.Ordinal);
        public static FloorModelDefinition[] Entries
        {
            get
            {
                if (entries == null)
                {
                    var source = Resources.Load<TextAsset>("FloorModels/catalog");
                    entries = source != null ? JsonUtility.FromJson<Catalog>(source.text)?.entries : null;
                    entries ??= Array.Empty<FloorModelDefinition>();
                }
                return entries;
            }
        }
        public static bool TryGet(string id, out FloorModelDefinition entry)
        { foreach (var value in Entries) if (value.id == id) { entry = value; return true; } entry = null; return false; }
        public static void Reload() => entries = null;
    }
}
