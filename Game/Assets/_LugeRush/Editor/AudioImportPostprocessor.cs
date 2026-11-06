using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Import rules for generated sounds: music and ambience stream as Vorbis, everything else is short
    /// ADPCM decompressed on load (cheap to start, safe to retrigger and to pitch every frame).
    /// </summary>
    public class AudioImportPostprocessor : AssetPostprocessor
    {
        public const string AudioRoot = "Assets/_LugeRush/Audio/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioRoot)) return;
            var importer = (AudioImporter)assetImporter;
            bool stream = assetPath.Contains("/Music/") || assetPath.Contains("/Ambience/");
            var s = importer.defaultSampleSettings;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            if (stream)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = assetPath.Contains("/Music/") ? 0.5f : 0.35f;
                s.preloadAudioData = false;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
                s.preloadAudioData = true;
            }
            importer.defaultSampleSettings = s;
            importer.loadInBackground = stream;
        }
    }
}
