//using CS2Cheat.Utils;
//using SkiaSharp;
//using Color = SharpDX.Color;
//
//namespace CS2Cheat.Features;
//
//internal class BombTimer(Graphics.Graphics graphics) : ThreadedServiceBase
//{
//    private static string _bombPlanted = string.Empty;
//    private static string _bombSite = string.Empty;
//    private static bool _isBombPlanted;
//    private static float _defuseLeft;
//    private static float _timeLeft;
//    private static float _defuseCountDown;
//    private static float _c4Blow;
//    private static float bomtime;
//    private static bool _beingDefused;
//
//    private IntPtr _globalVars;
//    private IntPtr _plantedC4;
//    private IntPtr _tempC4;
//
//    // NEW: keep proper time from server ticks
//    private float _intervalPerTick = 1.0f / 64.0f;   // fallback if read fails
//    private float _currentServerTime;
//    private int _lastServerTick;
//
//    protected override void FrameAction()
//    {
//        // --- read globals / interval ---
//        _globalVars = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwGlobalVars);
//        if (_globalVars != IntPtr.Zero)
//        {
//            // In CS2 this sits in GlobalVars; if your Offsets name differs, adjust
//            _intervalPerTick = graphics.GameProcess.Process.Read<float>(_globalVars + Offsets.m_nCurrentTickThisFrame);
//            if (_intervalPerTick <= 0f || _intervalPerTick > 0.1f) _intervalPerTick = 1.0f / 64.0f;
//        }
//
//        // --- get server tick safely from engine2 ---
//        try
//        {
//            var eng = graphics.GameProcess.ModuleEngine;
//            if (eng != null)
//            {
//                var ngc = eng.Read<IntPtr>(Offsets.engine2_dll.dwNetworkGameClient);
//                if (ngc != IntPtr.Zero)
//                {
//                    int tick = graphics.GameProcess.Process.Read<int>(
//                        ngc + Offsets.engine2_dll.dwNetworkGameClient_serverTickCount);
//
//                    // only move forward (prevents jitter/backwards snaps)
//                    if (tick >= _lastServerTick)
//                    {
//                        _currentServerTime = tick * _intervalPerTick;
//                        _lastServerTick = tick;
//                    }
//                }
//                else
//                {
//                    // crude fallback: advance by one tick per frame
//                    _currentServerTime += _intervalPerTick;
//                }
//            }
//            else
//            {
//                _currentServerTime += _intervalPerTick;
//            }
//        }
//        catch
//        {
//            _currentServerTime += _intervalPerTick;
//        }
//
//        // --- planted c4 chain ---
//        _tempC4 = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwPlantedC4);
//        if (_tempC4 == IntPtr.Zero) { _isBombPlanted = false; return; }
//
//        _plantedC4 = graphics.GameProcess.Process.Read<IntPtr>(_tempC4);
//        if (_plantedC4 == IntPtr.Zero) { _isBombPlanted = false; return; }
//
//        // Most reliable high-level flag
//        _isBombPlanted = graphics.GameProcess.ModuleClient.Read<bool>(Offsets.dwPlantedC4 - 0x8);
//        if (!_isBombPlanted) return;
//
//        // --- read timers/flags ---
//        _c4Blow = graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flC4Blow);
//
//        _defuseCountDown = graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flDefuseCountDown);
//
//        _beingDefused = graphics.GameProcess.Process.Read<bool>(_plantedC4 + Offsets.m_bBeingDefused);
//
//        _timeLeft = bomtime - _currentServerTime;
//        _defuseLeft = _beingDefused ? _defuseCountDown - _currentServerTime : 0f;
//
//        // expire
//        if (_timeLeft <= 0f)
//        {
//            _isBombPlanted = false;
//            return;
//        }
//
//        // site
//        _bombSite = (graphics.GameProcess.Process.Read<int>(_plantedC4 + Offsets.m_nBombSite) == 1) ? "B" : "A";
//        _bombPlanted = $"Bomb is planted on site: {_bombSite}";
//    }
//
//    public static void Draw(Graphics.Graphics graphics)
//    {
//        if (!_isBombPlanted) return;
//
//        graphics.FontAzonix64.DrawText(default, $"Bomb planted on site: {_bombSite}", 4, 500, Color.Orange);
//        graphics.FontAzonix64.DrawText(default, $"Time left: {_timeLeft:0.00} seconds", 4, 530, Color.OrangeRed);
//
//        if (_beingDefused && _defuseLeft > 0f)
//            graphics.FontAzonix64.DrawText(default, $"Defuse time: {_defuseLeft:0.00} seconds", 4, 560, Color.Cyan);
//    }
//}