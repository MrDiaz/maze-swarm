#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    // Only the two short, licensed Foley derivatives in this delivery.
    public sealed class CargoLeaperAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.EndsWith("/Resources/Audio/cargo_leaper_windup.wav") &&
                !assetPath.EndsWith("/Resources/Audio/cargo_leaper_pounce.wav")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true; importer.loadInBackground = false;
            var data = new SerializedObject(importer); var normalize = data.FindProperty("m_Normalize");
            if (normalize != null) { normalize.boolValue = false; data.ApplyModifiedPropertiesWithoutUndo(); }
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
#endif
