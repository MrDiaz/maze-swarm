#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Maze.EditorTools
{
    // Only the two new, already mastered CC0 first-floor cues. Existing assets are untouched.
    public class FirstCryoAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Audio/first_cryo_")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true; importer.loadInBackground = false;
            var data = new SerializedObject(importer); var normalize = data.FindProperty("m_Normalize");
            if (normalize != null) { normalize.boolValue = false; data.ApplyModifiedPropertiesWithoutUndo(); }
            var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
#endif
