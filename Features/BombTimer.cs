using CS2Cheat.Utils;
using Process.NET.Native.Types;
using static CS2Cheat.Utils.Offsets;
using Color = SharpDX.Color;

namespace CS2Cheat.Features
{
    internal class BombTimer(Graphics.Graphics graphics) : ThreadedServiceBase
    {
        private static string _bombPlanted = string.Empty;
        private static string _bombSite = string.Empty;
        private static bool _isBombPlanted;
        private static bool _will_defuse;
        private static bool _beingDefused;
        private static float _defuseLeft;
        private static float defuseLeft;
        private static float _timeLeft;
        private static float timeLeft;
        private static bool _isDefused;

        private IntPtr _globalVars;
        private IntPtr _plantedC4;

        private float _intervalPerTick = 1.0f / 64.0f;
        private float _currentServerTime;
        private static float _c4BlowStored = 0f;     // c4 blow time for this plant

        protected override void FrameAction()
        {
            // --- read globals / interval ---
            _globalVars = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwGlobalVars);
            _intervalPerTick = graphics.GameProcess.Process.Read<float>(_globalVars + Offsets.m_nCurrentTickThisFrame);
            _currentServerTime += (_intervalPerTick / 64) * 10;

            // <<< ИСПОЛЬЗУЕМ ТОЧНОЕ ВРЕМЯ ИЗ СЕРВЕРНЫХ ТИКОВ
            //var serverTickCount = graphics.GameProcess.Process.Read<int>(graphics.GameProcess.ModuleEngine.Read<IntPtr>(Offsets.engine2_dll.dwNetworkGameClient) + Offsets.engine2_dll.dwNetworkGameClient_serverTickCount);
            //_currentServerTime = serverTickCount * 0.015625f;

            // --- locate planted C4 base pointer ---
            var tempC4 = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.client_dll.dwPlantedC4);
            _plantedC4 = graphics.GameProcess.Process.Read<IntPtr>(tempC4);

            // --- planted flag ---
            _isBombPlanted = graphics.GameProcess.ModuleClient.Read<bool>(Offsets.dwPlantedC4 - 0x8);

            if (!_isBombPlanted)
            {
                // Bomb isn't planted anymore: if not defused, treat as round over
                if (_isDefused)
                {
                    // Keep defused status for a moment, but zero times
                    _timeLeft = 0f;
                    _defuseLeft = 0f;
                    _beingDefused = false;
                }
                return;
            }

            // --- bomb site ---
            int site = graphics.GameProcess.Process.Read<int>(_plantedC4 + Offsets.m_nBombSite);
            _bombSite = site == 1 ? "B" : "A";
            _bombPlanted = $"Bomb is planted on site: {_bombSite}";

            // --- read current blow time for this instance ---
            var c4Blow = graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flC4Blow);

            // --- new bomb instance detection & normalization ---
            if (Math.Abs(c4Blow - _c4BlowStored) > 0.01f)
            {
                // New plant detected – normalize timer:
                _c4BlowStored = c4Blow;
                _currentServerTime = _c4BlowStored - 40.0f;  // so timeLeft starts at ~40
            }

            // --- raw / clamped bomb time ---
            timeLeft = _c4BlowStored - _currentServerTime;
            _timeLeft = Math.Max(timeLeft, 0f);

            // --- defuse logic ---
            _beingDefused = graphics.GameProcess.Process.Read<bool>(_plantedC4 + Offsets.m_bBeingDefused);
            var defuseCountDown = graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flDefuseCountDown);

            defuseLeft = defuseCountDown - _currentServerTime;
            _defuseLeft = Math.Max(defuseLeft, 0f);

            // Can defuse?
            _will_defuse = _beingDefused && (c4Blow - _currentServerTime) > (_beingDefused ? defuseCountDown - _currentServerTime : 0f);

            // --- bomb defused flag from game ---
            bool defusedFlag = graphics.GameProcess.Process.Read<bool>(_plantedC4 + Offsets.m_bBombDefused);
            if (defusedFlag || (_beingDefused && _defuseLeft <= 0.001f))
            {
                MarkDefused();
                return;
            }
        }

        private static void MarkDefused()
        {
            _isDefused = true;
            _isBombPlanted = false;
            _beingDefused = false;
            _timeLeft = 0f;
            _defuseLeft = 0f;
        }

        public static void Draw(Graphics.Graphics graphics)
        {
            // nothing relevant
            if (!_isBombPlanted && !_isDefused) return;

            // Planted, active
            graphics.FontAzonix64.DrawText(default, $"Bomb planted on site: {_bombSite}", 4, 540, Color.Orange);

            if (_timeLeft > 0)
                graphics.FontAzonix64.DrawText(default, $"Time left: {_timeLeft:0.00} seconds", 4, 570, Color.OrangeRed);
            else
                graphics.FontAzonix64.DrawText(default, $"Time left: !!NO TIME!!", 4, 570, Color.OrangeRed);

            if (_beingDefused && _defuseLeft > 0f)
                graphics.FontAzonix64.DrawText(default, $"Defuse time: {_defuseLeft:0.00} seconds", 4, 600, Color.Cyan);

            if (_isDefused)
                graphics.FontAzonix64.DrawText(default, $"Defuse time: !!--Bomb DEFUSED--!!", 4, 600, Color.Cyan);

            if (_beingDefused)
                graphics.FontAzonix64.DrawText(default, $"Will Defuse: {_will_defuse}", 4, 630, Color.Cyan);
        }
    }
}
