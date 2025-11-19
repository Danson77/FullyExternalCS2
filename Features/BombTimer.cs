using CS2Cheat.Utils;
using Process.NET.Native.Types;
using static CS2Cheat.Utils.Offsets;
using Color = SharpDX.Color;

namespace CS2Cheat.Features;

internal class BombTimer(Graphics.Graphics graphics) : ThreadedServiceBase
{
    private static string _bombPlanted = string.Empty;
    private static string _bombSite = string.Empty;
    private static bool _isBombPlanted;
    private static float _defuseLeft;
    private static float _timeLeft;

    private static bool _beingDefused;
    private IntPtr _globalVars;
    private IntPtr _plantedC4;

    private float _intervalPerTick = 1.0f / 64.0f;
    private float _currentServerTime;
    private int _lastServerTick;
    protected override void FrameAction()
    {
        // Main Time Ticket
        // --- read globals / interval ---
        _globalVars = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwGlobalVars);

        // --- read globals / interval ---
        _globalVars = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwGlobalVars);
        _intervalPerTick = graphics.GameProcess.Process.Read<float>(_globalVars + Offsets.m_nCurrentTickThisFrame);

        var eng = graphics.GameProcess.ModuleEngine;
        if (eng != null)
        {
            var ngc = eng.Read<IntPtr>(Offsets.engine2_dll.dwNetworkGameClient);
            int tick = graphics.GameProcess.Process.Read<int>(ngc + Offsets.engine2_dll.dwNetworkGameClient_serverTickCount);

            // only move forward (prevents jitter/backwards snaps)
            if (tick >= _lastServerTick)
            {
                _currentServerTime = tick * _intervalPerTick;
                _lastServerTick = tick;
            }
        }
        else
        {
            _currentServerTime += _intervalPerTick;
        }

        // Main Is Planted
        var _tempC4 = graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.client_dll.dwPlantedC4);
        if (_tempC4 == IntPtr.Zero) return;
        _plantedC4 = graphics.GameProcess.Process.Read<IntPtr>(_tempC4);


        // Bomb Site
        _isBombPlanted = graphics.GameProcess.ModuleClient.Read<bool>(Offsets.dwPlantedC4 - 0x8);
        if (_isBombPlanted)
            _bombSite = graphics.GameProcess.Process.Read<int>(_plantedC4 + Offsets.m_nBombSite) == 1 ? "B" : "A";
            _bombPlanted = _isBombPlanted ? $"Bomb is planted on site: {_bombSite}" : string.Empty;

        // Bomb Time Left
        var _c4Blow = graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flC4Blow);
        _timeLeft = _c4Blow - _currentServerTime;
        _timeLeft = Math.Max(_timeLeft, 0);

        // Defusing
        var _defuseCountDown = graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flDefuseCountDown);
        _beingDefused = graphics.GameProcess.Process.Read<bool>(_plantedC4 + Offsets.m_bBeingDefused);
        _defuseLeft = _beingDefused ? _defuseCountDown - _currentServerTime : 0f;
        _defuseLeft = Math.Max(_defuseLeft, 0);
    }

    public static void Draw(Graphics.Graphics graphics)
    {
        if (!_isBombPlanted) return;

        graphics.FontAzonix64.DrawText(default, $"Bomb planted on site: {_bombSite}", 4, 500, Color.Orange);
        graphics.FontAzonix64.DrawText(default, $"Time left: {_timeLeft:0.00} seconds", 4, 530, Color.OrangeRed);

        if (_beingDefused && _defuseLeft > 0f)
            graphics.FontAzonix64.DrawText(default, $"Defuse time: {_defuseLeft:0.00} seconds", 4, 560, Color.Cyan);
    }
}