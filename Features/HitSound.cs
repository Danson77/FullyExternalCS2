using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using CS2Cheat.Utils.CFGManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace CS2Cheat.Features
{
    public static class HitSound
    {
        private static float _lastDamage = 0f;
        private static int _lastHsCount = 0;
        private static DateTime _lastCheck = DateTime.MinValue;
        private static readonly object _sync = new();

        private class HitText
        {
            public string Text { get; set; } = string.Empty;
            public DateTime ExpireAt { get; set; }
            public Vector2 BasePosition { get; set; }
            public float State { get; set; }
            public bool IsHeadshot { get; set; }
        }

        private static readonly List<HitText> _hitTexts = new();
        private static readonly object _textLock = new();

        public static void Process(Graphics.Graphics graphics)
        {
            var gp = graphics.GameProcess;
            var player = graphics.GameData.Player;

            if (gp?.Process == null || player == null || !player.IsAlive())
                return;

            // simple rate limit
            if ((DateTime.UtcNow - _lastCheck).TotalMilliseconds < 30)
                return;
            _lastCheck = DateTime.UtcNow;

            if (Offsets.client_dll.dwLocalPlayerController == 0 ||
                Offsets.m_pActionTrackingServices == 0 ||
                Offsets.m_flTotalRoundDamageDealt == 0)
                return;

            var localController = gp.ModuleClient?.Read<IntPtr>(Offsets.client_dll.dwLocalPlayerController) ?? IntPtr.Zero;
            if (localController == IntPtr.Zero) return;

            var actionTracking = gp.Process.Read<IntPtr>(localController + Offsets.m_pActionTrackingServices);
            if (actionTracking == IntPtr.Zero) return;

            float currentDamage;
            try { currentDamage = gp.Process.Read<float>(actionTracking + Offsets.m_flTotalRoundDamageDealt); }
            catch { return; }

            int currentHsCount = _lastHsCount;
            if (Offsets.m_iNumRoundKillsHeadshots != 0)
            {
                try { currentHsCount = gp.Process.Read<int>(actionTracking + Offsets.m_iNumRoundKillsHeadshots); }
                catch { /* ignore */ }
            }

            if (currentDamage <= _lastDamage)
            {
                _lastHsCount = currentHsCount;
                return;
            }

            int delta = (int)Math.Round(currentDamage - _lastDamage);
            if (delta < 1 || delta > 200)
            {
                _lastDamage = currentDamage;
                _lastHsCount = currentHsCount;
                return;
            }

            var cfg = ConfigManager.Load();
            var hsCfg = cfg.HitSound ?? new ConfigManager.HitSoundConfig();
            if (!hsCfg.Enabled)
            {
                _lastDamage = currentDamage;
                _lastHsCount = currentHsCount;
                return;
            }

            // headshot detection
            bool isHeadshot;
            if (Offsets.m_iNumRoundKillsHeadshots != 0)
                isHeadshot = currentHsCount > _lastHsCount; // direct check
            else
                isHeadshot = delta >= hsCfg.HeadshotDamageThreshold; // fallback

            string text = isHeadshot ? $"{hsCfg.HeadshotText} {delta}" : $"{hsCfg.HitText} {delta}";
            string soundFile = isHeadshot ? hsCfg.HeadshotSoundFile : hsCfg.HitSoundFile;

            // draw text center screen
            var rect = gp.WindowRectangleClient;
            if (rect.Width > 0 && rect.Height > 0)
            {
                var center = new Vector2(rect.Width / 2f, rect.Height / 2f);
                lock (_textLock)
                {
                    _hitTexts.Add(new HitText
                    {
                        Text = text,
                        BasePosition = center,
                        ExpireAt = DateTime.UtcNow.AddSeconds(hsCfg.TextDurationSeconds),
                        IsHeadshot = isHeadshot
                    });
                }
            }

            // play the correct sound immediately
            PlayHitSound(soundFile);

            lock (_sync)
            {
                _lastDamage = currentDamage;
                _lastHsCount = currentHsCount;
            }
        }

        public static void DrawHitTexts(Graphics.Graphics graphics)
        {
            var now = DateTime.UtcNow;
            lock (_textLock)
            {
                for (int i = _hitTexts.Count - 1; i >= 0; i--)
                {
                    var t = _hitTexts[i];
                    if (now > t.ExpireAt)
                    {
                        _hitTexts.RemoveAt(i);
                        continue;
                    }

                    t.State += 1f;
                    float offsetX = 15f * MathF.Sin(t.State / 30f);
                    float offsetY = -25f - (t.State * 1.4f);
                    var pos = new Vector2(t.BasePosition.X + offsetX, t.BasePosition.Y + offsetY);

                    float lifeMs = (float)(t.ExpireAt - now).TotalMilliseconds;
                    const float totalLife = 0.0001f;
                    float alpha = Math.Clamp(lifeMs / totalLife, 0.1f, 1f);

                    byte a = (byte)(255 * alpha);
                    var color = t.IsHeadshot
                        ? new SharpDX.Color((byte)0, (byte)255, (byte)0, a)   // green
                        : new SharpDX.Color((byte)255, (byte)0, (byte)0, a);  // red

                    // black outline (drawn behind)
                    var outlineColor = new SharpDX.Color((byte)0, (byte)0, (byte)0, a);
                    const int outlineSize = 1; // pixel thickness

                    graphics.FontConsolas36.DrawText(default, t.Text, (int)pos.X - outlineSize, (int)pos.Y, outlineColor);
                    graphics.FontConsolas36.DrawText(default, t.Text, (int)pos.X + outlineSize, (int)pos.Y, outlineColor);
                    graphics.FontConsolas36.DrawText(default, t.Text, (int)pos.X, (int)pos.Y - outlineSize, outlineColor);
                    graphics.FontConsolas36.DrawText(default, t.Text, (int)pos.X, (int)pos.Y + outlineSize, outlineColor);

                    // main text on top
                    graphics.FontConsolas36.DrawText(default, t.Text, (int)pos.X, (int)pos.Y, color);
                }
            }
        }


        private static void PlayHitSound(string soundFile)
        {
            try
            {
                // Let the mixer handle overlap and “no queue”
                AudioEngine.Play(soundFile, volume: 1.0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HitSound] Error: {ex.Message}");
            }
        }

    }
}
