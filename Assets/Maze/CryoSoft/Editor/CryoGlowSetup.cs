#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Maze.EditorTools
{
    // Assigns URP's default PostProcessData to the Maze renderer through the editor API (the room-scoped Cryo glow needs it;
    // post stays off on every camera unless the CryoSoft room switches it on).
    //   Unity -batchmode -quit -projectPath . -executeMethod Maze.EditorTools.CryoGlowSetup.Assign
    public static class CryoGlowSetup
    {
        public static void Assign()
        {
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Maze/Settings/MazeURP_Renderer.asset");
            var pp = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            Debug.Log($"CryoGlowSetup: renderer {(rd != null)}, postProcessData before {(rd != null && rd.postProcessData != null ? rd.postProcessData.name : "null")}, package asset {(pp != null ? pp.name : "null")}");
            if (rd == null || pp == null) return;
            rd.postProcessData = pp; EditorUtility.SetDirty(rd); AssetDatabase.SaveAssets();
            Debug.Log($"CryoGlowSetup: postProcessData after {(rd.postProcessData != null ? rd.postProcessData.name : "null")}; bloom shader {(pp.shaders.bloomPS != null ? pp.shaders.bloomPS.name : "null")}, uber {(pp.shaders.uberPostPS != null ? pp.shaders.uberPostPS.name : "null")}");
        }
    }
}
#endif
