using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;
using Object = UnityEngine.Object;

namespace Maze
{
    // Optional captured-pass evidence only. No allocation, Unity API, logging or I/O on the audio thread.
    [Preserve]
    public sealed class CohesiveQaAudioCapture : MonoBehaviour
    {
        [Serializable] public sealed class SourceSample
        { public float realtime; public int playingSources; public string[] clips; }
        [Serializable] public sealed class Metrics
        {
            public string schema = "cohesive-qa-mixed-audio-v1", utc, file, reason;
            public string conditions = "Actual AudioListener filter samples; final bounded 45 seconds, PCM16 clamped without normalization. AudioSource concurrency is a sampled proxy, not an attribution of masking. Captured-pass overhead only. No perceptual listening claim.";
            public bool completed, nonSilent, channelsChanged, sourceSamplingCapped;
            public int sampleRate, channels, callbackCount, maximumConcurrentSources;
            public long sampleFrames, totalFramesObserved, nearFullScaleSamples, overFullScaleSamples, nonFiniteSamples;
            public double durationSeconds, peak, rms;
            public float listenerVolume;
            public List<SourceSample> sourceSamples = new();
        }
        internal sealed class BoundedAudio
        {
            internal readonly object gate = new();
            internal readonly float[] ring;
            internal volatile bool accepting = true;
            internal int channels, write, filled, callbacks;
            internal long observedFrames;
            internal bool changed;
            internal BoundedAudio(int frames) { ring = new float[frames * 2]; }
            internal void Receive(float[] data, int count)
            {
                if (!accepting || count < 1 || count > 2) return;
                lock (gate)
                {
                    if (!accepting) return;
                    if (channels == 0) channels = count;
                    if (channels != count) { changed = true; return; }
                    callbacks++; observedFrames += data.Length / count;
                    int capacity = ring.Length / 2 * count;
                    for (int i = 0; i < data.Length; i++)
                    {
                        ring[write] = data[i]; write++;
                        if (write == capacity) write = 0;
                    }
                    filled = Math.Min(capacity, filled + data.Length);
                }
            }
            internal float[] Finish()
            {
                accepting = false;
                lock (gate)
                {
                    int count = channels > 0 ? filled - filled % channels : 0;
                    var result = new float[count];
                    int capacity = ring.Length / 2 * Math.Max(1, channels);
                    int first = (write - count + capacity) % capacity;
                    int left = Math.Min(count, capacity - first);
                    Array.Copy(ring, first, result, 0, left);
                    if (left < count) Array.Copy(ring, 0, result, left, count - left);
                    return result;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Environment.GetEnvironmentVariable("COHESIVE_QA") != "1" ||
                Environment.GetEnvironmentVariable("COHESIVE_QA_AUDIO") != "1" ||
                Environment.GetEnvironmentVariable("COHESIVE_QA_CAPTURES") == "0" ||
                Environment.GetEnvironmentVariable("COHESIVE_QA_MODE") == "editor-review") return;
            var root = new GameObject("Cohesive QA mixed audio (captured pass only)");
            DontDestroyOnLoad(root); root.AddComponent<CohesiveQaAudioCapture>();
        }

        BoundedAudio buffer;
        Metrics metrics;
        string output;
        float nextPoll, start;
        bool flushed;
        readonly HashSet<AudioListener> listeners = new();

        void Awake()
        {
            start = Time.realtimeSinceStartup;
            int frequency = AudioSettings.outputSampleRate;
            if (frequency <= 0 || frequency > 192000) { Destroy(gameObject); return; }
            metrics = new Metrics { sampleRate = frequency, utc = DateTime.UtcNow.ToString("O"), listenerVolume = AudioListener.volume };
            buffer = new BoundedAudio(frequency * 45);
        }

        void Update()
        {
            if (flushed || metrics == null || Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + .5f;
            try
            {
                if (output == null)
                {
                    string pointer = Path.Combine(Application.persistentDataPath, "CohesiveQA", "latest.txt");
                    if (!File.Exists(pointer)) return;
                    string candidate = File.ReadAllText(pointer).Trim();
                    string parent = Path.Combine(Application.persistentDataPath, "CohesiveQA");
                    if (Path.GetDirectoryName(candidate) != parent || !File.Exists(Path.Combine(candidate, "report.json"))) return;
                    var report = JsonUtility.FromJson<CohesivePhoneQa.Report>(File.ReadAllText(Path.Combine(candidate, "report.json")));
                    if (!CohesivePhoneQa.Active || !report.capturesEnabled || report.outputDirectory != candidate ||
                        !DateTime.TryParse(report.utc, out DateTime created) || (DateTime.UtcNow - created.ToUniversalTime()).TotalSeconds > 30) return;
                    output = candidate;
                }
                foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                {
                    if (!listener.enabled || !listener.gameObject.activeInHierarchy || listeners.Contains(listener)) continue;
                    var tap = listener.gameObject.AddComponent<CohesiveQaAudioTap>();
                    tap.Buffer = buffer; listeners.Add(listener);
                }
                var playing = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                    .Where(source => source.enabled && source.gameObject.activeInHierarchy && source.isPlaying).ToArray();
                metrics.maximumConcurrentSources = Math.Max(metrics.maximumConcurrentSources, playing.Length);
                if (metrics.sourceSamples.Count < 1800)
                    metrics.sourceSamples.Add(new SourceSample { realtime = Time.realtimeSinceStartup, playingSources = playing.Length,
                        clips = playing.Take(40).Select(source => source.clip != null ? source.clip.name : "<stream/filter>").ToArray() });
                else metrics.sourceSamplingCapped = true;
                var current = JsonUtility.FromJson<CohesivePhoneQa.Report>(File.ReadAllText(Path.Combine(output, "report.json")));
                if (current.completed) Flush("Runtime QA completed", true);
            }
            catch (IOException) { /* A report checkpoint may be in the middle of a main-thread write; retry. */ }
            catch (ArgumentException) { /* Retry partial JSON checkpoints without affecting game state. */ }
        }

        void Flush(string reason, bool completed)
        {
            if (flushed || buffer == null || output == null) return;
            flushed = true;
            float[] data = buffer.Finish(); // Audio thread is now stopped; all remaining work is on the main thread.
            metrics.completed = completed; metrics.reason = reason;
            metrics.channels = buffer.channels; metrics.callbackCount = buffer.callbacks;
            metrics.channelsChanged = buffer.changed; metrics.totalFramesObserved = buffer.observedFrames;
            metrics.sampleFrames = metrics.channels > 0 ? data.LongLength / metrics.channels : 0;
            metrics.durationSeconds = (double)metrics.sampleFrames / metrics.sampleRate;
            double square = 0;
            foreach (float sample in data)
            {
                if (float.IsNaN(sample) || float.IsInfinity(sample)) { metrics.nonFiniteSamples++; continue; }
                double absolute = Math.Abs((double)sample);
                metrics.peak = Math.Max(metrics.peak, absolute); square += sample * (double)sample;
                if (absolute >= .9999) metrics.nearFullScaleSamples++;
                if (absolute > 1) metrics.overFullScaleSamples++;
            }
            metrics.rms = data.Length > 0 ? Math.Sqrt(square / data.Length) : 0;
            metrics.nonSilent = metrics.rms > .00001;
            metrics.file = Path.Combine(output, "mixed-audio.wav");
            using (var stream = File.Create(metrics.file))
            using (var writer = new BinaryWriter(stream))
            {
                int channels = Math.Max(1, metrics.channels), bytes = data.Length * 2;
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); writer.Write(36 + bytes);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(metrics.sampleRate);
                writer.Write(metrics.sampleRate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(bytes);
                foreach (float sample in data)
                {
                    float finite = float.IsNaN(sample) || float.IsInfinity(sample) ? 0 : sample;
                    writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, finite)) * 32767));
                }
            }
            File.WriteAllText(Path.Combine(output, "mixed-audio-metrics.json"), JsonUtility.ToJson(metrics, true));
            Debug.Log("Cohesive QA mixed audio flushed: " + metrics.file + "; no listening claim.");
        }
        void OnApplicationQuit() { Flush("App quit before QA completion", false); }
        void OnDestroy() { Flush("Audio evidence component destroyed", false); }
    }

    [Preserve]
    public sealed class CohesiveQaAudioTap : MonoBehaviour
    {
        internal volatile CohesiveQaAudioCapture.BoundedAudio Buffer;
        void OnAudioFilterRead(float[] data, int channels) { Buffer?.Receive(data, channels); }
    }
}
