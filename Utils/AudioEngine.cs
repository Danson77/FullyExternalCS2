using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CS2Cheat.Features
{
    public static class AudioEngine
    {
        // Tweakables
        private const int MAX_VOICES = 6;                 // cap simultaneous shots
        private const int OUTPUT_SAMPLE_RATE = 48000;     // match your WAVs to this
        private const int OUTPUT_CHANNELS = 1;            // mono is ideal for SFX
        private const int LATENCY_MS = 90;                // 80–100ms is safe on most rigs

        private static readonly object _initLock = new();
        private static bool _initialized;
        private static IWavePlayer? _output;
        private static MixingSampleProvider? _mixer;
        private static ISampleProvider? _masterChain;     // mixer -> limiter -> (optional) volume
        private static readonly ConcurrentDictionary<string, CachedSound> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static volatile int _activeVoices = 0;

        public static void Init()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;

                var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(OUTPUT_SAMPLE_RATE, OUTPUT_CHANNELS);
                _mixer = new MixingSampleProvider(mixFormat) { ReadFully = true };

                // Master soft limiter to prevent clipping when many voices stack
                var limiter = new SoftLimiter(_mixer, thresholdDb: -1.0f, releaseMs: 60f);

                // Optional overall volume trim (keep headroom)
                var masterVol = new VolumeSampleProvider(limiter) { Volume = 0.9f };

                _masterChain = masterVol;

                var wo = new WaveOutEvent
                {
                    DesiredLatency = LATENCY_MS,
                    NumberOfBuffers = 3
                };
                wo.Init(_masterChain);
                wo.Play();
                _output = wo;

                _initialized = true;
            }
        }

        public static void Dispose()
        {
            lock (_initLock)
            {
                _output?.Stop();
                _output?.Dispose(); _output = null;
                _mixer = null;
                _masterChain = null;
                _cache.Clear();
                _initialized = false;
            }
        }

        public static void Play(string path, float volume = 0.6f)
        {
            if (!_initialized) Init();
            if (_mixer == null) return;

            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path)) return;
            if (_activeVoices >= MAX_VOICES) return;  // no queue: drop if saturated

            var cached = _cache.GetOrAdd(path, p => new CachedSound(p, OUTPUT_SAMPLE_RATE, OUTPUT_CHANNELS));

            var voice = new CachedSoundSampleProvider(cached);
            ISampleProvider provider = voice;

            if (Math.Abs(volume - 1.0f) > 0.001f)
                provider = new VolumeSampleProvider(provider) { Volume = volume };

            System.Threading.Interlocked.Increment(ref _activeVoices);
            voice.OnPlaybackStopped += () => System.Threading.Interlocked.Decrement(ref _activeVoices);

            _mixer.AddMixerInput(provider);
        }

        // ========= Cached sound preconverted to mixer format =========
        private sealed class CachedSound
        {
            public float[] AudioData { get; }
            public WaveFormat WaveFormat { get; }

            public CachedSound(string audioFilePath, int targetRate, int targetChannels)
            {
                // Decode and convert ONCE to mixer format; store float[] buffer.
                using var reader = new AudioFileReader(audioFilePath); // handles wav/mp3
                ISampleProvider sample = reader; // float provider

                // Channel convert if needed
                if (reader.WaveFormat.Channels != targetChannels)
                {
                    if (targetChannels == 1)
                        sample = new StereoToMonoSampleProvider(sample);
                    else
                        sample = new MonoToStereoSampleProvider(sample); // for completeness
                }

                // Resample if needed
                if (reader.WaveFormat.SampleRate != targetRate)
                    sample = new WdlResamplingSampleProvider(sample, targetRate);

                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetRate, targetChannels);

                var all = new List<float>((int)(reader.Length / 4));
                var buf = new float[targetRate * targetChannels / 5]; // ~200ms chunk
                int read;
                while ((read = sample.Read(buf, 0, buf.Length)) > 0)
                    all.AddRange(buf.AsSpan(0, read));
                AudioData = all.ToArray();
            }
        }

        private sealed class CachedSoundSampleProvider : ISampleProvider
        {
            private readonly CachedSound _cached;
            private long _pos;
            public event Action? OnPlaybackStopped;

            public CachedSoundSampleProvider(CachedSound cached)
            {
                _cached = cached;
                WaveFormat = cached.WaveFormat;
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                var remaining = _cached.AudioData.Length - _pos;
                if (remaining <= 0) { OnPlaybackStopped?.Invoke(); return 0; }

                var toCopy = (int)Math.Min(remaining, count);
                Array.Copy(_cached.AudioData, _pos, buffer, offset, toCopy);
                _pos += toCopy;
                if (_pos >= _cached.AudioData.Length) OnPlaybackStopped?.Invoke();
                return toCopy;
            }
        }

        // ========= Simple soft limiter to prevent output clipping =========
        private sealed class SoftLimiter : ISampleProvider
        {
            private readonly ISampleProvider _src;
            private readonly float _thrLin;
            private readonly float _releaseCoeff;
            private float _gain = 1f;

            public SoftLimiter(ISampleProvider src, float thresholdDb = -1.0f, float releaseMs = 60f)
            {
                _src = src;
                WaveFormat = src.WaveFormat;
                _thrLin = DbToLin(thresholdDb);
                // One-pole release towards 1.0 gain
                _releaseCoeff = (float)Math.Exp(-1.0 / (WaveFormat.SampleRate * (releaseMs / 1000.0)));
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int n = _src.Read(buffer, offset, count);
                for (int i = 0; i < n; i++)
                {
                    var s = buffer[offset + i] * _gain;

                    // If we exceed threshold, reduce gain quickly
                    float abs = Math.Abs(s);
                    if (abs > _thrLin)
                    {
                        float needed = _thrLin / (abs + 1e-9f);
                        _gain = MathF.Min(_gain, needed); // fast attack
                        s = buffer[offset + i] * _gain;
                    }

                    // Gentle release back towards 1
                    _gain = 1f - (1f - _gain) * _releaseCoeff;

                    // Soft clip safety (tanh)
                    buffer[offset + i] = MathF.Tanh(s * 1.2f);
                }
                return n;
            }

            private static float DbToLin(float db) => (float)Math.Pow(10.0, db / 20.0);
        }
    }
}
