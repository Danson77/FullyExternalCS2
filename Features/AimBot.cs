using CS2Cheat.Core;
using CS2Cheat.Core.Data;
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
        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
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

        // ---------------- Toggles ----------------
        private static bool targetTeammates = false;
        private static Keys TKKey = Keys.OemQuotes;
        private bool isTKToggled = false;

        private static bool isAimLockEnabled = true;
        private static Keys AimLockToggleKey = Keys.OemOpenBrackets;
        private bool isAimLockToggled = false;

        public AimBot(GameProcess gameProcess, GameData gameData)
        {
            GameProcess = gameProcess;
            GameData = gameData;
            MouseHook = new GlobalHook(HookType.WH_MOUSE_LL, MouseHookCallback);
            KeyboardHook = new GlobalHook(HookType.WH_KEYBOARD_LL, KeyboardHookCallback);
        }

        // ---------------- Aimbot math ----------------
        private static readonly float SeekFov = 3.5f.DegreeToRadian();
        private const float MovingToTargetSmoothness = 22.0f;

        // tune these (rad-based) A + D straff movment adjsutment
        private const float StrafeSpeedThreshold = 23f;
        private const float StrafeYawGain = 0.045f;   // rad * meters / unit-speed  (linear small-angle model)
        private const float StrafeMaxRad = 0.20f;    // max  ~11.5°

        private double _yawPerPixel = 0.0;   // radians per pixel
        private double _pitchPerPixel = 0.0; // radians per pixel
        // ---------------- Helpers ----------------

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

        private static float AngleDiffRad(float a, float b)
        {
            float d = a - b;
            while (d > MathF.PI) d -= 2 * MathF.PI;
            while (d < -MathF.PI) d += 2 * MathF.PI;
            return d;
        }


        private static bool GetTargetPoint(Player me, out float pitchRad, out float yawRad)
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

        private static double NextGaussian()
        {
            // Box–Muller transform -> normal distribution (μ=0, σ=1)
            double u = 1.0 - _rnd.NextDouble();
            double v = 1.0 - _rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
        }
        private void TargetPixels(Vector2 aimAngles, out Point aimPixels)
        {
            if (aimAngles == Vector2.Zero)
            {
                aimPixels = new Point(0, 0);
                return;
            }

            // convert radians → pixels with smoothing
            double pxX = (aimAngles.X / _yawPerPixel) / MovingToTargetSmoothness;
            double pxY = (aimAngles.Y / _pitchPerPixel) / MovingToTargetSmoothness;

            //(simulates a micro-swing arc instead of perfect straight line)
            double dist = Math.Sqrt(pxX * pxX + pxY * pxY);
            if (dist > 0.6)
            {
                double curveAngle = NextGaussian() * 0.6; // radians ~ small arc
                double cosA = Math.Cos(curveAngle);
                double sinA = Math.Sin(curveAngle);
            
                double newX = pxX * cosA - pxY * sinA;
                double newY = pxX * sinA + pxY * cosA;
                pxX = newX;
                pxY = newY;
            }

            // Keep rounding at the very end
            int moveX = (int)Math.Truncate(pxX);
            int moveY = (int)Math.Truncate(pxY);

            aimPixels = new Point(moveX, moveY);
        }

        private void TargetAngles(Vector3 targetPosition, out float angleSize, out Vector2 aimAngles, GameData gameData)
        {
            Vector3 eyePos = gameData.Player.EyePosition;
            Vector3 aimDir = Vector3.Normalize(gameData.Player.EyeDirection);
            Vector3 toTarget = targetPosition - eyePos;
            Vector3 desFwd = Vector3.Normalize(targetPosition - eyePos);

            // current yaw/pitch from EyeDirection
            float curYaw = YawFromDir(aimDir);
            float curPitch = PitchFromDir(aimDir);

            // ---- AimPunch compensation (un-kick the current view) ----
            if (GetTargetPoint(gameData.Player, out float punchPitch, out float punchYaw))
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
            float dYaw = AngleDiffRad(desYaw, curYaw) * -1;
            float dPitch = AngleDiffRad(desPitch, curPitch) * +1;

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

            aimAngles = new Vector2(dYaw, dPitch);
            angleSize = MathF.Sqrt(dYaw * dYaw + dPitch * dPitch);
        }

        // read engine opinion of crosshair index (if offsets exist)
        private bool ReadEngineCrosshairIndex(Player? me, out int idx)
        {
            idx = 0;
            if (me == null) return false;
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
        private bool TryGetEntityIndex(Entity? e, out int val)
        {
            val = 0;
            if (e == null) return false;
            var t = e.GetType();
            foreach (var n in new[] { "Index", "EntityIndex", "ControllerIndex", "PawnIndex", "ID", "Id", "m_iIndex", "iEntityIndex" })
            {
                try
                {
                    var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p?.GetValue(e) is object pv && pv != null) { try { val = Convert.ToInt32(pv); return true; } catch { } }
                    var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f?.GetValue(e) is object fv && fv != null) { try { val = Convert.ToInt32(fv); return true; } catch { } }
                }
                catch { }
            }
            return false;
        }

        private bool TargetLocked(out Vector2 aimAngles)
        {
            aimAngles = Vector2.Zero;
            var now = DateTime.Now;
            var me = GameData.Player;
            IntPtr bestAddr = IntPtr.Zero;
            Vector2 bestAngles = Vector2.Zero;

            // engine LOS gating (if dump offsets available)
            if (Offsets.m_iIDEntIndex != 0)
            {
                if (!ReadEngineCrosshairIndex(me, out var crossIdx) || crossIdx <= 0)
                {
                    return false;
                }
            
                // find entity with that index
                foreach (var ent in GameData.Entities)
                {
            
                    if (ent == null) continue;
            
                    if (!TryGetEntityIndex(ent, out var idx) || idx != crossIdx) continue;
            
                }
            }

            foreach (var e in GameData.Entities)
            {
                if (!e.IsAlive()) continue;
                if (e.AddressBase == me.AddressBase) continue;
                if (!targetTeammates && e.Team == me.Team) continue;

                Vector3 pos;
                if (
                    !e.BonePos.TryGetValue("head", out pos) &&
                    !e.BonePos.TryGetValue("neck_0", out pos) &&
                    !e.BonePos.TryGetValue("spine_1", out pos) &&
                    !e.BonePos.TryGetValue("spine_2", out pos) &&
                    !e.BonePos.TryGetValue("pelvis", out pos) &&
                    !e.BonePos.TryGetValue("clavicle_L", out pos) &&
                    !e.BonePos.TryGetValue("clavicle_R", out pos) &&

                    !e.BonePos.TryGetValue("arm_upper_L", out pos) &&
                    !e.BonePos.TryGetValue("arm_lower_L", out pos) &&
                    !e.BonePos.TryGetValue("hand_L", out pos) &&

                    !e.BonePos.TryGetValue("arm_upper_R", out pos) &&
                    !e.BonePos.TryGetValue("arm_lower_R", out pos) &&
                    !e.BonePos.TryGetValue("hand_R", out pos) &&

                    !e.BonePos.TryGetValue("leg_upper_L", out pos) &&
                    !e.BonePos.TryGetValue("leg_lower_L", out pos) &&
                    !e.BonePos.TryGetValue("ankle_L", out pos) &&
                    !e.BonePos.TryGetValue("ball_L", out pos) &&
                    !e.BonePos.TryGetValue("toe_L", out pos) &&

                    !e.BonePos.TryGetValue("leg_upper_R", out pos) &&
                    !e.BonePos.TryGetValue("leg_lower_R", out pos) &&
                    !e.BonePos.TryGetValue("ankle_R", out pos) &&
                    !e.BonePos.TryGetValue("ball_R", out pos) &&
                    !e.BonePos.TryGetValue("toe_R", out pos) &&

                    !e.BonePos.TryGetValue("finger_thumb_0_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_thumb_1_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_thumb_2_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_thumb_end_L", out pos) &&

                    !e.BonePos.TryGetValue("finger_index_meta_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_0_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_1_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_2_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_end_L", out pos) &&

                    !e.BonePos.TryGetValue("finger_middle_meta_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_0_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_1_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_2_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_end_L", out pos) &&

                    !e.BonePos.TryGetValue("finger_ring_meta_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_0_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_1_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_2_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_end_L", out pos) &&

                    !e.BonePos.TryGetValue("finger_pinky_meta_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_0_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_1_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_2_L", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_end_L", out pos) &&

                    !e.BonePos.TryGetValue("finger_thumb_0_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_thumb_1_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_thumb_2_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_thumb_end_R", out pos) &&

                    !e.BonePos.TryGetValue("finger_index_meta_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_0_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_1_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_2_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_index_end_R", out pos) &&

                    !e.BonePos.TryGetValue("finger_middle_meta_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_0_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_1_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_2_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_middle_end_R", out pos) &&

                    !e.BonePos.TryGetValue("finger_ring_meta_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_0_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_1_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_2_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_ring_end_R", out pos) &&

                    !e.BonePos.TryGetValue("finger_pinky_meta_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_0_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_1_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_2_R", out pos) &&
                    !e.BonePos.TryGetValue("finger_pinky_end_R", out pos)
                )
                continue;


                TargetAngles(pos, out var a, out var ang, GameData);

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

        private static bool MoveMouseTest(Point aimPixels)
        {
            int dx = aimPixels.X;
            int dy = aimPixels.Y;
            if (dx == 0 && dy == 0) return false;

            double dist = Math.Sqrt(dx * dx + dy * dy);

            // perform the move
            Utility.MouseMove(dx, dy);

            return true;
        }
        private static bool MouseMoveMain(Point aimPixels)
        {
            if (aimPixels.X == 0 && aimPixels.Y == 0) return false;

            if (Math.Abs(aimPixels.X) > 100 || Math.Abs(aimPixels.Y) > 100) return false;
            Utility.WindMouseMove(0, 0, aimPixels.X, aimPixels.Y, 9.0, 3.0, 15.0, 12.0);
            return true;
        }

        // ---------------- Frame loop ----------------
        protected override void FrameAction()
        {
            HandleToggles();

            if (!GameProcess.IsValid || !GameData.Player.IsAlive())
                return;

            if (!isAimLockEnabled)
                return;

            if (!IsCalibrated && ShouldCalibrate())
            {
                Calibrate();
                IsCalibrated = true;
            }

            if (!TargetLocked(out var aimAngles))
                return;

            TargetPixels(aimAngles, out var aimPixels);

            MouseMoveMain(aimPixels);
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