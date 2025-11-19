using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using CS2Cheat.Core;
using SharpDX; // Vector3
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using static CS2Cheat.Utils.Utility;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features
{
    public class TriggerBot : ThreadedServiceBase
    {
        public TriggerBot(GameProcess proc, GameData data) { GameProcess = proc; GameData = data; }

        // ------------------ Win32 / Timing ------------------
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private static bool IsMouseDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long NowMs => Clock.ElapsedMilliseconds;

        // ------------------ Runtime State ------------------
        private long _holdStartMs = -1;
        private long _holdUntilMs = -1;
        private long _nextFireAllowedAtMs = 0;
        private bool _holding, _weInitiated;
        private Entity? _focusTarget;

        private readonly GameProcess GameProcess;
        private readonly GameData GameData;
        private readonly Random _rnd = new();

        // ------------------ Config / Constants ------------------
        public const int HoldMsBase = 120;
        public const int CooldownMsBase = 200;

        // vertical speed limit (don’t trigger while flying / big jumps)
        private const float MaxVelocityZ = 18f;

        // teammate blocker radius and depth margin (left here in case you re-use later)
        public const float teammateRadius = 7.0f;
        public const float depthMargin = 2.0f;

        // how close to the *edge* of the hit-sphere we are allowed to fire
        // 1.0 = full radius (loose), 0.5 = only center half (very tight)
        public const float HitTightness = 0.75f;   // unused now, but kept

        // ------------------ Toggles ------------------
        private static bool _triggerEnabled = true;
        private static bool _teamTriggerEnabled = false;
        private static readonly Keys TriggerToggleKey = Keys.OemCloseBrackets; // ]
        private static readonly Keys TeamToggleKey = Keys.OemQuotes;           // '
        private static bool _triggerLatch, _teamLatch;

        protected override string ThreadName => nameof(TriggerBot);

        // ------------------ Toggles ------------------
        private void HandleToggles()
        {
            if (TriggerToggleKey.IsKeyDown() && !_triggerLatch)
            {
                _triggerEnabled = !_triggerEnabled;
                _triggerLatch = true;
                Console.WriteLine($"[Trigger] Enabled: {_triggerEnabled}");
                if (!_triggerEnabled) StopHold(false);
            }
            else if (!TriggerToggleKey.IsKeyDown() && _triggerLatch) _triggerLatch = false;

            if (TeamToggleKey.IsKeyDown() && !_teamLatch)
            {
                _teamTriggerEnabled = !_teamTriggerEnabled;
                _teamLatch = true;
                Console.WriteLine($"[Trigger] TeamTrigger: {_teamTriggerEnabled}");
            }
            else if (!TeamToggleKey.IsKeyDown() && _teamLatch) _teamLatch = false;
        }

        // ------------------ Helpers ------------------
        // Attempts to read a numeric team value from various common fields/properties
        private static int GetTeam(object? obj)
        {
            if (obj == null) return 0;

            try
            {
                var t = obj.GetType();
                // all likely team-related field/property names
                string[] names = { "Team", "m_iTeamNum", "iTeamNum", "team" };

                foreach (var n in names)
                {
                    try
                    {
                        var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (p?.GetValue(obj) is object pv)
                            return Convert.ToInt32(pv);

                        var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f?.GetValue(obj) is object fv)
                            return Convert.ToInt32(fv);
                    }
                    catch { }
                }
            }
            catch { }

            return 0; // default if nothing found
        }

        // engine-style entity lookup from crosshair index (dwEntityList pattern)
        private IntPtr GetCrosshairEntityPtr()
        {
            if (!GameProcess.IsValid) return IntPtr.Zero;
            if (GameProcess.ModuleClient == null) return IntPtr.Zero;
            if (GameProcess.Process == null) return IntPtr.Zero;

            // 1) local pawn
            var localPawn = GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerPawn);
            if (localPawn == IntPtr.Zero) return IntPtr.Zero;

            // 2) engine crosshair entity index
            int entityId = GameProcess.Process.Read<int>(localPawn + Offsets.m_iIDEntIndex);
            if (entityId < 0) return IntPtr.Zero;

            // 3) resolve index -> entity via dwEntityList
            var entityList = GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwEntityList);
            if (entityList == IntPtr.Zero) return IntPtr.Zero;

            // standard CS2 pattern
            IntPtr entry = GameProcess.Process.Read<IntPtr>(
                entityList + 0x8 * (entityId >> 9) + 0x10);
            if (entry == IntPtr.Zero) return IntPtr.Zero;

            IntPtr entPtr = GameProcess.Process.Read<IntPtr>(
                entry + 112 * (entityId & 0x1FF));
            return entPtr;
        }

        // ---------- Targeting (simplified: use engine crosshair entity) ----------
        private bool FindTarget(out Entity? target, out bool isAlly)
        {
            target = null;
            isAlly = false;

            var me = GameData.Player;
            if (me == null || !me.IsAlive()) return false;

            // optional: don’t trigger when flying / huge vertical speed
            if (Math.Abs(me.Velocity.Z) > MaxVelocityZ)
                return false;

            int myTeam = GetTeam(me);
            if (myTeam == 0)
            {
                // fallback in case GetTeam fails
                try { myTeam = (int)me.Team; } catch { myTeam = 0; }
            }

            // ask the engine: who is under the crosshair?
            IntPtr entPtr = GetCrosshairEntityPtr();
            if (entPtr == IntPtr.Zero) return false;

            // read target team
            int entityTeam = GameProcess.Process.Read<int>(entPtr + Offsets.m_iTeamNum);
            if (entityTeam <= 0) return false;

            // teammate?
            if (entityTeam == myTeam)
            {
                if (!_teamTriggerEnabled)
                {
                    // teammate, and we’re not allowed to shoot teammates -> no target
                    return false;
                }

                // team trigger is enabled; we treat this as ally target
                isAlly = true;
            }
            else
            {
                // normal enemy target
                isAlly = false;
            }

            // Try to map raw pointer -> Entity object from GameData.Entities
            foreach (var e in GameData.Entities)
            {
                if (e == null) continue;
                if (e.AddressBase == (long)entPtr)
                {
                    target = e;
                    break;
                }
            }

            // Even if we fail to map to Entity, we still have a valid enemy in crosshair.
            // For your current logic, you only care about "haveTarget" bool.
            return true;
        }

        // ------------------ Firing ------------------
        public static void MouseLeftDown()
        {
            var inputs = new Input[1];

            inputs[0] = new Input
            {
                Type = InputType.Mouse,
                Union = new InputUnion
                {
                    mouse = new MouseInput
                    {
                        flags = MouseFlags.LeftDown
                    }
                }
            };
            User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        }

        public static void MouseLeftUp()
        {
            var inputs = new Input[1];

            inputs[0] = new Input
            {
                Type = InputType.Mouse,
                Union = new InputUnion
                {
                    mouse = new MouseInput
                    {
                        flags = MouseFlags.LeftUp
                    }
                }
            };

            User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        }

        private void StartHold()
        {
            if (IsMouseDown()) return;
            MouseLeftDown();
            _holding = true;
            _weInitiated = true;
            _holdStartMs = NowMs;
            _holdUntilMs = _holdStartMs + HoldMsBase + _rnd.Next(-20, 25);
        }

        private void StopHold(bool applyCooldown)
        {
            if (_weInitiated) MouseLeftUp();
            _holding = false;
            _weInitiated = false;
            _focusTarget = null;
            if (applyCooldown) _nextFireAllowedAtMs = NowMs + CooldownMsBase + _rnd.Next(-40, 60);
        }

        // ---------- main loop ----------
        protected override void FrameAction()
        {
            HandleToggles();
            if (!_triggerEnabled || !GameProcess.IsValid)
            {
                if (_holding) StopHold(false);
                return;
            }

            var me = GameData.Player;
            if (me == null || !me.IsAlive())
            {
                if (_holding) StopHold(false);
                return;
            }

            if (NowMs < _nextFireAllowedAtMs)
            {
                if (_holding) StopHold(false);
                return;
            }

            bool haveTarget = FindTarget(out var tgt, out _);
            if (!_holding && haveTarget)
            {
                _focusTarget = tgt;
                StartHold();
                return;
            }

            if (_holding)
            {
                if (NowMs >= _holdUntilMs) StopHold(true);
                else if (!haveTarget) StopHold(true);
            }
        }
    }
}