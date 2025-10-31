using CS2Cheat.Core;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using Process.NET.Native.Types;
using SharpDX;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Keys = Process.NET.Native.Types.Keys;
using Point = System.Drawing.Point;

namespace CS2Cheat.Features
{
    public class AimBot : ThreadedServiceBase
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        // ---------------- Hooks/state ----------------
        private DateTime lastMovementTime = DateTime.MinValue;
        private IntPtr _currentTargetBase = IntPtr.Zero;
        private float _yawFiringBase = 0f;
        private float _pitchFiringBase = 0f;
        private const int VK_A = 0x41;
        private const int VK_D = 0x44;
        private bool isFiring = false;
        private static readonly Random _rnd = new Random();
        protected override string ThreadName => nameof(AimBot);
        private GlobalHook MouseHook { get; set; }
        private GlobalHook KeyboardHook { get; set; }
        private GameProcess GameProcess { get; set; }
        private GameData GameData { get; set; }
        private bool IsCalibrated { get; set; }
        private static float YawFromDir(Vector3 fwd) => MathF.Atan2(fwd.Y, fwd.X);
        private static float PitchFromDir(Vector3 fwd) => -MathF.Atan2(fwd.Z, MathF.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y));
        private static float Deg2Rad(float d) => d * (float)(Math.PI / 180.0);
        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public AimBot(GameProcess gameProcess, GameData gameData)
        {
            GameProcess = gameProcess;
            GameData = gameData;
            MouseHook = new GlobalHook(HookType.WH_MOUSE_LL, MouseHookCallback);
            KeyboardHook = new GlobalHook(HookType.WH_KEYBOARD_LL, KeyboardHookCallback);
        }
        private bool ProcessMouseMessage(MouseMessages mouseMessage)
        {
            if (mouseMessage == MouseMessages.WmMouseMove)
                lastMovementTime = DateTime.Now;

            if (mouseMessage == MouseMessages.WmLButtonDown)
            {
                isFiring = true;
                HoldLeftShift();
            }

            if (mouseMessage == MouseMessages.WmLButtonUp)
            {
                isFiring = false;
                ReleaseLeftShift();
            }
            return true;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool shouldProcess = ProcessMouseMessage((MouseMessages)wParam);
                if (shouldProcess)
                    return User32.CallNextHookEx(MouseHook.HookHandle, nCode, wParam, lParam);
                return new IntPtr(1);
            }
            return User32.CallNextHookEx(MouseHook.HookHandle, nCode, wParam, lParam);
        }
        // ---------------- Toggles ----------------
        private static bool targetTeammates = false;
        private static Keys TKKey = Keys.OemQuotes;
        private bool isTKToggled = false;

        private static bool isAimLockEnabled = true;
        private static Keys AimLockToggleKey = Keys.OemOpenBrackets;
        private bool isAimLockToggled = false;

        // ---------------- Aimbot math ----------------
        private static string AimBonePos => "head";

        private const float MovingToSmooth = 3.0f;

        private static readonly float SeekFov = 4.5f.DegreeToRadian();
        private static readonly float BreakAwayAngle = 0.3f.DegreeToRadian(); //0.15

        // tune these (rad-based) A + D straff movment adjsutment
        private const float StrafeSpeedThreshold = 23f;     // already in your class
        private const float StrafeYawGain = -0.045f;   // rad * meters / unit-speed  (linear small-angle model)
        private const float StrafeMaxRad = 0.20f;    // max  ~11.5°

        private double _yawPerPixel = 0.0;   // radians per pixel
        private double _pitchPerPixel = 0.0; // radians per pixel

        // ---------------- Helpers ----------------
        private static float AngleDiffRad(float a, float b)
        {
            float d = a - b;
            while (d > MathF.PI) d -= 2 * MathF.PI;
            while (d < -MathF.PI) d += 2 * MathF.PI;
            return d;
        }

        private static double NextGaussian()
        {
            // Box–Muller transform -> normal distribution (μ=0, σ=1)
            double u = 1.0 - _rnd.NextDouble();
            double v = 1.0 - _rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
        }

        private bool IsVisible(Entity ent, Player me, float angleToBone, Vector3 bonePos, out string reason)
        {
            reason = "";
            if (TryGetSpottedMask(ent, out var mask) && TryGetMyBit(me, out var myBit))
            {
                bool vis = ((mask & (1UL << myBit)) != 0);
                var now = DateTime.Now;

                if (vis) { reason = "SPOTTED"; return true; }
            }
            return false;
        }

        private static bool TryGetAimPunch(Player me, out float pitchRad, out float yawRad)
        {
            pitchRad = 0f; yawRad = 0f;
            var t = me.GetType();
            object? val = null;

            // Look for AimPunch-like property/field
            foreach (var n in new[] { "AimPunchAngle", "m_aimPunchAngle", "AimPunch", "aimPunch" })
            {
                var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) { val = p.GetValue(me); break; }

                var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { val = f.GetValue(me); break; }
            }

            if (val == null) return false;

            // Support Vector2 or Vector3 (X=pitch, Y=yaw in Source)
            if (val is SharpDX.Vector2 v2)
            {
                pitchRad = Deg2Rad(v2.X);
                yawRad = Deg2Rad(v2.Y);
            }
            else if (val is SharpDX.Vector3 v3)
            {
                pitchRad = Deg2Rad(v3.X);
                yawRad = Deg2Rad(v3.Y);
            }
            else
            {
                return false; // unknown type
            }

            pitchRad *= 2.0f;
            yawRad *= 2.0f;
            return true;
        }

        private nint GetClientBase()
        {
            try
            {
                var proc = GameProcess.Process;
                if (proc == null || proc.HasExited) return 0;
                foreach (ProcessModule m in proc.Modules)
                    if (string.Equals(m.ModuleName, "client.dll", StringComparison.OrdinalIgnoreCase))
                        return (nint)m.BaseAddress;
            }
            catch { }
            return 0;
        }

        private static bool TryGetSpottedMask(Entity e, out ulong mask)
        {
            mask = 0UL;
            var t = e.GetType();

            var f = t.GetField("SpottedByMask", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? t.GetField("m_bSpottedByMask", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                var v = f.GetValue(e);
                if (v is ulong u) { mask = u; return true; }
                if (v is long l) { mask = unchecked((ulong)l); return true; }
                if (v is uint u32) { mask = u32; return true; }
            }

            var p = t.GetProperty("SpottedByMask", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? t.GetProperty("m_bSpottedByMask", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                var v = p.GetValue(e);
                if (v is ulong u) { mask = u; return true; }
                if (v is long l) { mask = unchecked((ulong)l); return true; }
                if (v is uint u32) { mask = u32; return true; }
            }
            return false;
        }

        private static bool TryGetIndexLike(object obj, out int idx)
        {
            idx = 0;
            var t = obj.GetType();
            foreach (var n in new[] { "EntityIndex", "PawnIndex", "Index", "ControllerIndex", "ID", "Id", "m_iIndex" })
            {
                var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var v = p.GetValue(obj);
                    if (v is int i) { idx = i; return true; }
                    if (v is short s) { idx = s; return true; }
                    if (v is ushort us) { idx = us; return true; }
                }
                var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v is int i2) { idx = i2; return true; }
                    if (v is short s2) { idx = s2; return true; }
                    if (v is ushort us2) { idx = us2; return true; }
                }
            }
            return false;
        }

        private static bool TryGetMyBit(Player me, out int myBitZeroBased) // 0..63
        {
            myBitZeroBased = 0;
            if (!TryGetIndexLike(me, out var idx) || idx <= 0) return false;
            myBitZeroBased = (idx - 1) & 63;
            return true;
        }

        // Strict LOS, returns true only when we got a >0 index
        private bool EngineLOS(Player me, out int idx)
        {
            idx = 0;

            if (Offsets.m_iIDEntIndex == 0)
            {
                idx = 1; // degrade to allow if offset is missing
                return true;
            }

            try
            {
                var clBase = GetClientBase();
                if (clBase != 0 && Offsets.dwLocalPlayerPawn != 0)
                {
                    nint lpAddr = (nint)(clBase + Offsets.dwLocalPlayerPawn);
                    nint lpawn = Utility.Read<nint>(GameProcess.Process, lpAddr);
                    if (lpawn != 0)
                    {
                        idx = Utility.Read<int>(GameProcess.Process, lpawn + Offsets.m_iIDEntIndex);
                        if (idx > 0) return true;
                    }
                }
            }
            catch { }

            try
            {
                nint pawnBase = (nint)me.AddressBase;
                if (pawnBase != 0)
                {
                    idx = Utility.Read<int>(GameProcess.Process, pawnBase + Offsets.m_iIDEntIndex);
                    if (idx > 0) return true;
                }
            }
            catch { }

            return false;
        }

        // add: returns pixel error magnitude through out param
        private void GetAimPixels(Vector2 aimAngles, out Point aimPixels)
        {
            if (aimAngles == Vector2.Zero)
            {
                aimPixels = new Point(0, 0);
                return;
            }

            // convert radians → pixels with smoothing
            double pxX = (aimAngles.X / _yawPerPixel) / MovingToSmooth;
            double pxY = (aimAngles.Y / _pitchPerPixel) / MovingToSmooth;

            float fov = Player.Fov;
            float zoomScale = (90f / fov);
            zoomScale = MathF.Sqrt(zoomScale);  // realistic zoom scaling
            pxX *= zoomScale;
            pxY *= zoomScale;

            // =========================
            // 🧠 Human Irregularity Layer
            // =========================

            // 1. Slight random non-linear path curvature
            //    (simulates a micro-swing arc instead of perfect straight line)
            double dist = Math.Sqrt(pxX * pxX + pxY * pxY);
            if (dist > 0.1)
            {
                double curveAngle = NextGaussian() * 0.08; // radians ~ small arc
                double cosA = Math.Cos(curveAngle);
                double sinA = Math.Sin(curveAngle);

                double newX = pxX * cosA - pxY * sinA;
                double newY = pxX * sinA + pxY * cosA;
                pxX = newX;
                pxY = newY;
            }

            // 2. Human mis-centering / under-aim or over-aim effect
            double bias = NextGaussian() * 0.05;  // ±5% total overshoot chance
            pxX *= (1.0 + bias);
            pxY *= (1.0 + bias * 0.8);

            // 3. Slight "reaction hesitation" – small fraction of motion skipped
            if (_rnd.NextDouble() < 0.15)
            {
                pxX *= 0.8 + _rnd.NextDouble() * 0.2;  // 80–100% of the intended step
                pxY *= 0.8 + _rnd.NextDouble() * 0.2;
            }

            // 4. Rare “micro overshoot then correct” prep — 5% chance
            if (_rnd.NextDouble() < 0.05)
            {
                pxX += Math.Sign(pxX) * _rnd.Next(1, 3);
                pxY += Math.Sign(pxY) * _rnd.Next(1, 3);
            }

            // 5. Keep rounding at the very end
            int moveX = (int)Math.Truncate(pxX);
            int moveY = (int)Math.Truncate(pxY);

            aimPixels = new Point(moveX, moveY);
        }

        private void GetAimAngles(Vector3 targetPosition, out float angleSize, out Vector2 aimAngles, GameData gameData)
        {
            Vector3 eyePos = gameData.Player.EyePosition;
            Vector3 aimDir = Vector3.Normalize(gameData.Player.EyeDirection);
            Vector3 toTarget = targetPosition - eyePos;
            Vector3 desFwd = Vector3.Normalize(targetPosition - eyePos);

            // current yaw/pitch from EyeDirection
            float curYaw = YawFromDir(aimDir);
            float curPitch = PitchFromDir(aimDir);

            // ---- AimPunch compensation (un-kick the current view) ----
            if (TryGetAimPunch(gameData.Player, out float punchPitch, out float punchYaw))
            {
                if (isFiring)
                {
                    // Source: X=pitch, Y=yaw
                    curYaw += punchYaw;
                    curPitch += punchPitch;
                }
                else
                {
                    // not firing: reset baseline to current punch (usually near zero)
                    _yawFiringBase = punchYaw;
                    _pitchFiringBase = punchPitch;
                }
            }

            // desired yaw/pitch
            float desYaw = YawFromDir(desFwd);
            float desPitch = PitchFromDir(desFwd);

            // deltas (radians)
            float dYaw = AngleDiffRad(desYaw, curYaw);
            float dPitch = AngleDiffRad(desPitch, curPitch);

            // ---- Strafe compensation (same rad space) ----
            Vector3 strafeRight = Vector3.Normalize(Vector3.Cross(aimDir, new Vector3(0, 0, 1)));
            float lateralSpeed = Vector3.Dot(gameData.Player.Velocity, strafeRight);
            float distXY = MathF.Max(1f, MathF.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y));

            if ((IsKeyDown(VK_A) || IsKeyDown(VK_D)) || MathF.Abs(lateralSpeed) > StrafeSpeedThreshold)
            {
                // linearized small-angle lead; tweak StrafeYawGain sign if you see wrong direction
                float compRad = (lateralSpeed * StrafeYawGain) / distXY;
                compRad = Math.Clamp(compRad, -StrafeMaxRad, StrafeMaxRad);
                dYaw -= compRad;
            }

            // map to your mouse/sign conventions
            const int YawSign = -1;   // flip to +1 if your build expects the other way
            const int PitchSign = +1; // flip if vertical inverted

            aimAngles = new Vector2(YawSign * dYaw, PitchSign * dPitch);
            angleSize = MathF.Sqrt(dYaw * dYaw + dPitch * dPitch);
        }

        // --- visible-first picking, hidden-only seek clamp + per-visibility smoothing ---
        private bool GetAimTarget(out Vector2 aimAngles)
        {
            aimAngles = Vector2.Zero;
            var now = DateTime.Now;
            var me = GameData.Player;

            // ---------- maintain lock ----------
            if (_currentTargetBase != IntPtr.Zero)
            {
                Entity locked = null;
                foreach (var e in GameData.Entities)
                    if (e.AddressBase == _currentTargetBase) { locked = e; break; }

                if (locked != null && locked.IsAlive() && (targetTeammates || locked.Team != me.Team))
                {
                    Vector3 lpos;
                    if (!locked.BonePos.TryGetValue(AimBonePos, out lpos) &&
                        !locked.BonePos.TryGetValue("spine_2", out lpos) &&
                        !locked.BonePos.TryGetValue("pelvis", out lpos))
                    {
                        _currentTargetBase = IntPtr.Zero;
                    }
                    else
                    {
                        GetAimAngles(lpos, out var lAngle, out var lAngles, GameData);

                        // b) crosshair moved off this entity → drop
                        if (EngineLOS(me, out var crossIdx) && crossIdx > 0 &&
                            TryGetIndexLike(locked, out var lockedIdx) && lockedIdx > 0 &&
                            crossIdx != lockedIdx)
                        {
                            _currentTargetBase = IntPtr.Zero;
                            return false;
                        }

                        // c) user-intent break (A/D pressed and you're looking away enough)
                        if (lAngle > BreakAwayAngle)
                        {
                            _currentTargetBase = IntPtr.Zero;
                            return false;
                        }

                        // scan competitors
                        foreach (var cand in GameData.Entities)
                        {
                            if (!cand.IsAlive()) continue;
                            if (cand.AddressBase == me.AddressBase) continue;
                            if (!targetTeammates && cand.Team == me.Team) continue;

                            Vector3 cpos;
                            if (!cand.BonePos.TryGetValue(AimBonePos, out cpos) &&
                                !cand.BonePos.TryGetValue("spine_2", out cpos) &&
                                !cand.BonePos.TryGetValue("pelvis", out cpos)) continue;

                            GetAimAngles(cpos, out var cAngle, out var cAngles, GameData);

                            // seeking
                            float cAllowed = SeekFov;
                            if (cAngle > cAllowed) continue;
                        }

                        // drop and re-acquire
                        _currentTargetBase = IntPtr.Zero;
                    }
                }
                else
                {
                    _currentTargetBase = IntPtr.Zero;
                }
            }

            // ---------- PASS ----------
            IntPtr bestAddr = IntPtr.Zero;
            Vector2 bestAngles = Vector2.Zero;

            foreach (var e in GameData.Entities)
            {
                if (!e.IsAlive()) continue;
                if (e.AddressBase == me.AddressBase) continue;
                if (!targetTeammates && e.Team == me.Team) continue;

                Vector3 pos;
                if (!e.BonePos.TryGetValue(AimBonePos, out pos) &&
                    !e.BonePos.TryGetValue("spine_2", out pos) &&
                    !e.BonePos.TryGetValue("pelvis", out pos)) continue;

                GetAimAngles(pos, out var a, out var ang, GameData);
                bool vis = IsVisible(e, me, a, pos, out _);

                if (a > SeekFov) continue;   // keep your hidden-only clamp

                bestAddr = e.AddressBase;
                bestAngles = ang;
            }

            if (bestAddr != IntPtr.Zero)
            {
                _currentTargetBase = bestAddr;
                aimAngles = bestAngles;
                return true;
            }

            return false;
        }

        // ---------------- Frame loop ----------------
        protected override void FrameAction()
        {
            HandleToggles();

            if (!GameProcess.IsValid || !GameData.Player.IsAlive())
                return;

            if (!IsCalibrated && ShouldCalibrate())
            {
                Calibrate();
                IsCalibrated = true;
            }

            if (!isAimLockEnabled)
                return;

            if (!GetAimTarget(out var aimAngles))
                return;

            // compute pixels + error magnitude
            GetAimPixels(aimAngles, out var aimPixels);

            // choose mover based on distance from center
            TryMouseMoveHuman(aimPixels);
        }

        // ---------------- Toggles ----------------
        private void HandleToggles()
        {
            if (TKKey.IsKeyDown() && !isTKToggled)
            {
                targetTeammates = !targetTeammates;
                Console.WriteLine($"[+]Team Aim Lock Enabled:{targetTeammates}");
                isTKToggled = true;
            }
            else if (!TKKey.IsKeyDown() && isTKToggled)
            {
                isTKToggled = false;
            }

            if (AimLockToggleKey.IsKeyDown() && !isAimLockToggled)
            {
                isAimLockEnabled = !isAimLockEnabled;
                Console.WriteLine($"[+]AimLock: {isAimLockEnabled}");
                isAimLockToggled = true;
            }
            else if (!AimLockToggleKey.IsKeyDown() && isAimLockToggled)
            {
                isAimLockToggled = false;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            MouseHook?.Dispose(); MouseHook = null;
            KeyboardHook?.Dispose(); KeyboardHook = null;
            GameData = null;
            GameProcess = null;
        }

        // ---------------- Mouse hook ----------------
        private static bool TryMouseMoveInsta(Point aimPixels)
        {
            if (aimPixels.X == 0 && aimPixels.Y == 0) return false;
            Utility.MouseMove(aimPixels.X, aimPixels.Y);
            return true;
        }

        private static bool TryMouseMoveHuman(Point aimPixels)
        {
            int dx = aimPixels.X;
            int dy = aimPixels.Y;
            if (dx == 0 && dy == 0) return false;

            double dist = Math.Sqrt(dx * dx + dy * dy);

            // --- HUMAN HAND DRIFT ---
            // small slow oscillation even when "locked"
            double t = Environment.TickCount / 1000.0;
            double driftX = Math.Sin(t * 20.0 + _rnd.NextDouble());
            double driftY = Math.Sin(t * 5.0 + _rnd.NextDouble());

            // occasional overshoot or under-aim
            if (_rnd.NextDouble() < 0.12)
            {
                dx += Math.Sign(dx) * _rnd.Next(-1, 2);
                dy += Math.Sign(dy) * _rnd.Next(-1, 2);
            }

            // combine base correction + drift
            dx += (int)Math.Round(driftX);
            dy += (int)Math.Round(driftY);

            // clamp so it never jumps unrealistically
            dx = Math.Clamp(dx, -4, 4);
            dy = Math.Clamp(dy, -4, 4);

            // perform the move
            Utility.MouseMove(dx, dy);

            // variable micro delay to break timing regularity
            Thread.Sleep(_rnd.Next(0, 3));
            return true;
        }

        // ---------------- Keyboard hook ----------------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const int VK_LSHIFT = 0xA0;
        private bool isLeftShiftHeld = false;

        private void HoldLeftShift()
        {
            if (!isLeftShiftHeld)
            {
                keybd_event(0xA0, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                isLeftShiftHeld = true;
            }
        }

        private void ReleaseLeftShift()
        {
            if (isLeftShiftHeld)
            {
                keybd_event(0xA0, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                isLeftShiftHeld = false;
            }
        }

        private bool ProcessKeyboardMessage(Keys keyMessage)
        {
            if ((int)keyMessage == VK_LSHIFT)
            {
                if (isFiring && !isLeftShiftHeld) HoldLeftShift();
                else if (!isFiring && isLeftShiftHeld) ReleaseLeftShift();
            }
            return true;
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool shouldProcess = ProcessKeyboardMessage((Keys)wParam);
                if (shouldProcess)
                    return User32.CallNextHookEx(KeyboardHook.HookHandle, nCode, wParam, lParam);
                return new IntPtr(1);
            }
            return User32.CallNextHookEx(KeyboardHook.HookHandle, nCode, wParam, lParam);
        }

        // ---------------- Calibration ----------------
        private bool ShouldCalibrate()
        {
            if (!GameData.Player.IsAlive())
                return false;
            bool hasRecentMovement = (DateTime.Now - lastMovementTime).TotalSeconds < 1;
            return hasRecentMovement;
        }

        private double MeasureYawPerPixel(int dx)
        {
            Thread.Sleep(50);
            float yaw0 = YawFromDir(GameData.Player.EyeDirection);
            Utility.MouseMove(dx, 0);
            Thread.Sleep(50);
            float yaw1 = YawFromDir(GameData.Player.EyeDirection);
            Utility.MouseMove(-dx, 0); // restore
            return Math.Abs(AngleDiffRad(yaw1, yaw0)) / Math.Abs(dx);
        }

        private double MeasurePitchPerPixel(int dy)
        {
            Thread.Sleep(50);
            float p0 = PitchFromDir(GameData.Player.EyeDirection);
            Utility.MouseMove(0, dy);
            Thread.Sleep(50);
            float p1 = PitchFromDir(GameData.Player.EyeDirection);
            Utility.MouseMove(0, -dy); // restore
            return Math.Abs(AngleDiffRad(p1, p0)) / Math.Abs(dy);
        }

        private void Calibrate()
        {
            var yawSamples = new[]
            {
        MeasureYawPerPixel(+400), MeasureYawPerPixel(-400),
        MeasureYawPerPixel(+600), MeasureYawPerPixel(-600)
    };
            var pitchSamples = new[]
            {
        MeasurePitchPerPixel(+300), MeasurePitchPerPixel(-300),
        MeasurePitchPerPixel(+500), MeasurePitchPerPixel(-500)
    };
            _yawPerPixel = Math.Max(1e-6, yawSamples.Average());
            _pitchPerPixel = Math.Max(1e-6, pitchSamples.Average());
            Console.WriteLine($"[+] Calibrated yaw={_yawPerPixel:F6} rad/px, pitch={_pitchPerPixel:F6} rad/px");
        }
    }
}