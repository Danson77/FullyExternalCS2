using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using CS2Cheat.Core;
using SharpDX; // Vector2, Vector3
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

        // recoil filter: only shoot when recoil is small / early in spray
        private const int MaxShotsFired = 2;      // allow first 1–2 bullets only
        private const float MaxPunchLen = 1.8f;   // how much aim punch we tolerate

        // teammate blocker radius / margin (kept in case you reuse later)
        public const float teammateRadius = 7.0f;
        public const float depthMargin = 2.0f;

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
        private static int GetTeam(object? obj)
        {
            if (obj == null) return 0;

            try
            {
                var t = obj.GetType();
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

            return 0;
        }

        // ---- REFLECTION HELPERS (same idea as your aimbot) ----
        private static bool TryGetAimPunch(object me, out Vector2 punch)
        {
            punch = Vector2.Zero;
            if (me == null) return false;

            var t = me.GetType();
            object? val = null;

            // Look for AimPunch-like property/field
            foreach (var n in new[] { "AimPunchAngle", "m_aimPunchAngle", "AimPunch", "aimPunch" })
            {
                var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    val = p.GetValue(me);
                    if (val != null) break;
                }

                var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    val = f.GetValue(me);
                    if (val != null) break;
                }
            }

            if (val == null) return false;

            // Try to coerce whatever we found into SharpDX.Vector2
            switch (val)
            {
                case Vector2 v2:
                    punch = v2;
                    return true;

                case Vector3 v3:
                    punch = new Vector2(v3.X, v3.Y);
                    return true;

                case System.Numerics.Vector2 nv2:
                    punch = new Vector2(nv2.X, nv2.Y);
                    return true;

                case System.Numerics.Vector3 nv3:
                    punch = new Vector2(nv3.X, nv3.Y);
                    return true;

                default:
                    try
                    {
                        // last-resort: convert via dynamic if the type is similar
                        var xProp = val.GetType().GetProperty("X");
                        var yProp = val.GetType().GetProperty("Y");
                        if (xProp != null && yProp != null)
                        {
                            float x = Convert.ToSingle(xProp.GetValue(val));
                            float y = Convert.ToSingle(yProp.GetValue(val));
                            punch = new Vector2(x, y);
                            return true;
                        }
                    }
                    catch { }
                    break;
            }

            return false;
        }

        private static bool TryGetShotsFired(object me, out int shots)
        {
            shots = 0;
            if (me == null) return false;

            var t = me.GetType();
            object? val = null;

            foreach (var n in new[] { "ShotsFired", "m_iShotsFired", "shotsFired" })
            {
                var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    val = p.GetValue(me);
                    if (val != null) break;
                }

                var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    val = f.GetValue(me);
                    if (val != null) break;
                }
            }

            if (val == null) return false;

            try
            {
                shots = Convert.ToInt32(val);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---- recoil “stability” check using reflection-based punch + shots ----
        private bool IsRecoilStable(Player me)
        {
            // Shots fired
            int shotsFired = 0;
            if (TryGetShotsFired(me, out var s))
                shotsFired = s;

            // Too deep into spray → skip trigger
            if (shotsFired >= MaxShotsFired)
                return false;

            // Aim punch magnitude
            if (TryGetAimPunch(me, out var punch))
            {
                float len = punch.Length();
                if (len > MaxPunchLen)
                    return false;
            }

            // If we can't read punch at all, we still at least gated by shots fired.
            return true;
        }

        // engine-style entity lookup from crosshair index (dwEntityList pattern)
        private IntPtr GetCrosshairEntityPtr()
        {
            if (!GameProcess.IsValid) return IntPtr.Zero;
            if (GameProcess.ModuleClient == null) return IntPtr.Zero;
            if (GameProcess.Process == null) return IntPtr.Zero;

            var localPawn = GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerPawn);
            if (localPawn == IntPtr.Zero) return IntPtr.Zero;

            int entityId = GameProcess.Process.Read<int>(localPawn + Offsets.m_iIDEntIndex);
            if (entityId < 0) return IntPtr.Zero;

            var entityList = GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwEntityList);
            if (entityList == IntPtr.Zero) return IntPtr.Zero;

            IntPtr entry = GameProcess.Process.Read<IntPtr>(
                entityList + 0x8 * (entityId >> 9) + 0x10);
            if (entry == IntPtr.Zero) return IntPtr.Zero;

            IntPtr entPtr = GameProcess.Process.Read<IntPtr>(
                entry + 112 * (entityId & 0x1FF));
            return entPtr;
        }

        // ---------- Targeting (simplified: engine crosshair entity) ----------
        private bool FindTarget(out Entity? target, out bool isAlly)
        {
            target = null;
            isAlly = false;

            var me = GameData.Player;
            if (me == null || !me.IsAlive()) return false;

            // don’t trigger while flying / huge vertical speed
            if (Math.Abs(me.Velocity.Z) > MaxVelocityZ)
                return false;

            int myTeam = GetTeam(me);
            if (myTeam == 0)
            {
                try { myTeam = (int)me.Team; } catch { myTeam = 0; }
            }

            IntPtr entPtr = GetCrosshairEntityPtr();
            if (entPtr == IntPtr.Zero) return false;

            int entityTeam = GameProcess.Process.Read<int>(entPtr + Offsets.m_iTeamNum);
            if (entityTeam <= 0) return false;

            if (entityTeam == myTeam)
            {
                if (!_teamTriggerEnabled)
                    return false;

                isAlly = true;
            }
            else
            {
                isAlly = false;
            }

            foreach (var e in GameData.Entities)
            {
                if (e == null) continue;
                if (e.AddressBase == (long)entPtr)
                {
                    target = e;
                    break;
                }
            }

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

            // recoil-aware gate using the same AimPunch reflection logic as aimbot
            if (!IsRecoilStable(me))
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
