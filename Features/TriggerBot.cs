using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using SharpDX; // Vector3
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features
{
    public class TriggerBot : ThreadedServiceBase
    {
        // ------------------ Win32 / Timing ------------------
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private static bool IsMouseDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long NowMs => Clock.ElapsedMilliseconds;
        private static readonly Vector3 WorldUp = new(0, 0, 1);

        // ------------------ Config / Constants ------------------
        public const int HoldMsBase = 120;
        public const int CooldownMsBase = 200;

        public const float WiggleNear = 2.1f;
        public const float WiggleFar = 4.2f;
        public const float WiggleScaleMaxDist = 1500f;

        public const float AllyHalfWidth = 10f;

        public const float BodyHalfStep = 6f;
        public const int BodyThicknesss = 2;
        public const float AllyWiggleMul = 1.2f;
        public const float OccluderDepthMargin = 2.5f;

        // ------------------ Toggles ------------------
        private static bool _triggerEnabled = true;
        private static bool _teamTriggerEnabled = false;
        private static readonly Keys TriggerToggleKey = Keys.OemCloseBrackets; // ]
        private static readonly Keys TeamToggleKey = Keys.OemQuotes;           // '
        private static bool _triggerLatch, _teamLatch;

        // ------------------ Runtime State ------------------
        private long _holdStartMs = -1;
        private long _holdUntilMs = -1;
        private long _nextFireAllowedAtMs = 0;
        private bool _holding, _weInitiated;
        private Entity? _focusTarget;

        private readonly GameProcess GameProcess;
        private readonly GameData GameData;
        private readonly Random _rnd = new();

        protected override string ThreadName => nameof(TriggerBot);
        public TriggerBot(GameProcess proc, GameData data) { GameProcess = proc; GameData = data; }

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
        private static float Clamp(float v) => MathF.Max(0f, MathF.Min(1f, v));
        //private static float Clamp(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static bool IsHeadBone(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            return name.Contains("head") || name.Contains("neck") || name.Contains("bip01_head") || name.Contains("hitbox_head");
        }

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

        // Attempts many common id-like fields to find an entity index
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


        // Relaxed and smoothed thresholds
        public const float thicknessTolerance = 6.5f; // wider beam = less flicker
        public const float depthSafetyMargin = 5.0f;  // avoid false occlusion on equal depth

        // Checks if the aim ray towards target is obstructed by any teammate or solid entity closer than the target
        private bool IsOccluded(Player me, Entity target, float targetDepth, Vector3 dirNorm)
        {
            int myTeam = GetTeam(me);
            if (myTeam == 0) return false;

            // lateral unit (side) vector
            var lateralUnit = Vector3.Cross(dirNorm, WorldUp);

            foreach (var ent in GameData.Entities)
            {
                if (ent == null) continue;
                if (ReferenceEquals(ent, target)) continue;
                if (!ent.IsAlive()) continue; // skip ragdolls / dead entities

                //if (GetTeam(ent) != myTeam) continue;

                var bones = ent.BonePos;
                if (bones == null || bones.Count == 0) continue;

                int entTeam = GetTeam(ent);
                bool isTeammate = (entTeam != 0 && entTeam == myTeam);
                bool isNeutral = (entTeam == 0); // props, neutrals

                // Only check occlusion for teammates or neutrals (not enemies)
                if (!isTeammate && !isNeutral) continue;

                foreach (var kv in bones)
                {
                    string bname = kv.Key ?? string.Empty;
                    if (!IsHeadBone(bname)) continue;
                    Vector3 basePos = kv.Value;

                    Span<Vector3> lateral = stackalloc Vector3[3]
                    {
                        basePos,
                        basePos + lateralUnit * AllyHalfWidth,
                        basePos - lateralUnit * AllyHalfWidth
                    };

                    for (int j = -BodyThicknesss; j <= BodyThicknesss; j++)
                    {
                        float up = j * BodyHalfStep;
                        foreach (var lp in lateral)
                        {
                            var p = lp + WorldUp * up;
                            var op = p - me.EyePosition;
                            float boneDepth = Vector3.Dot(op, dirNorm);

                            // Ignore if behind or beyond target
                            if (boneDepth <= 0f || boneDepth > targetDepth - depthSafetyMargin) continue;

                            var closest = me.EyePosition + dirNorm * boneDepth;
                            float pd = (p - closest).Length();


                            // scale wiggle by depth
                            float t01 = Clamp(boneDepth / WiggleScaleMaxDist);
                            float wiggle = (WiggleNear + (WiggleFar - WiggleNear) * t01) * AllyWiggleMul;
                        }
                    }
                }
            }
            return false;
        }

        // ---------- Targeting ----------
        private bool FindTarget(out Entity? target, out bool isAlly)
        {
            target = null;
            isAlly = false;

            var me = GameData.Player;
            if (me == null || !me.IsAlive()) return false;

            var eyePos = me.EyePosition;
            var dirNorm = Vector3.Normalize(me.EyeDirection);
            int myTeam = GetTeam(me);

            Entity? bestEnemy = null;
            float bestEnemyDepth = float.MaxValue, bestEnemyPD = float.MaxValue;
            Entity? bestAlly = null;
            float bestAllyDepth = float.MaxValue, bestAllyPD = float.MaxValue;

            const float eps = 1e-3f;
            static bool Nearer(float newDepth, float newPD, float curDepth, float curPD, float eps)
            {
                return (newDepth + eps < curDepth) ||
                       (Math.Abs(newDepth - curDepth) <= eps && newPD < curPD);
            }

            foreach (var ent in GameData.Entities)
            {
                if (ent == null || !ent.IsAlive()) continue;
                if (ent.AddressBase == me.AddressBase) continue;

                int entTeam = GetTeam(ent);
                bool friend = (myTeam != 0 && entTeam != 0 && entTeam == myTeam);

                var bones = ent.BonePos;
                if (bones == null || bones.Count == 0) continue;

                var right = Vector3.Cross(dirNorm, WorldUp);

                foreach (var kv in bones)
                {
                    string boneName = kv.Key ?? string.Empty;
                    Vector3 basePos = kv.Value;

                    if (!friend)
                    {
                        // For each enemy bone, sweep down as well as at bone
                        int feetSweep = 2; // try 2-3 for thicker capsule
                        float feetStep = -8f; // step size downward

                        for (int k = 0; k <= feetSweep; k++)
                        {
                            var p = basePos + WorldUp * (k * feetStep);
                            var op = p - eyePos;
                            float depth = Vector3.Dot(op, dirNorm);
                            if (depth <= 0f) continue;

                            var closest = eyePos + dirNorm * depth;
                            float pDist = (p - closest).Length();

                            float t01 = Clamp(depth / WiggleScaleMaxDist);
                            float wiggle = WiggleNear + (WiggleFar - WiggleNear) * t01;

                            if (pDist <= wiggle && Nearer(depth, pDist, bestEnemyDepth, bestEnemyPD, eps))
                            {
                                bestEnemyDepth = depth; bestEnemyPD = pDist; bestEnemy = ent;
                            }
                        }

                    }
                    else
                    {
                        // ally: body-wide capsule (all bones, lateral ring + short vertical sweep)
                        Span<Vector3> lateral = stackalloc Vector3[3]
                        {
                            basePos,
                            basePos + right * AllyHalfWidth,
                            basePos - right * AllyHalfWidth
                        };
                        for (int j = -BodyThicknesss; j <= BodyThicknesss; j++)
                        {
                            float up = j * BodyHalfStep;
                            foreach (var lp in lateral)
                            {
                                var p = lp + WorldUp * up;

                                var op = p - eyePos;
                                float depth = Vector3.Dot(op, dirNorm);
                                if (depth <= 0f) continue;

                                var closest = eyePos + dirNorm * depth;
                                float pDist = (p - closest).Length();

                                float t01 = Clamp(depth / WiggleScaleMaxDist);
                                float wiggle = (WiggleNear + (WiggleFar - WiggleNear) * t01) * AllyWiggleMul;

                                if (pDist <= wiggle && Nearer(depth, pDist, bestAllyDepth, bestAllyPD, eps))
                                {
                                    bestAllyDepth = depth; bestAllyPD = pDist; bestAlly = ent;
                                }
                            }
                        }
                    }
                }
            }

            // nothing matched
            if (bestEnemy == null && (bestAlly == null || !_teamTriggerEnabled)) return false;

            // if ally capsule is closer than enemy -> block (unless candidate is that ally and teamTriggerEnabled)
            if (bestEnemy != null && bestAlly != null && bestAllyDepth + OccluderDepthMargin < bestEnemyDepth) return false;

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
                    int entTeam = GetTeam(ent);

                    if (ent == null) continue;

                    if (!TryGetEntityIndex(ent, out var idx) || idx != crossIdx) continue;

                    // if teamTriggerEnabled and engine resolved a teammate, pick them (but still check occluders)
                    if (_teamTriggerEnabled && entTeam != 0 && entTeam == myTeam)
                    {
                        target = ent;
                        isAlly = true;
                        return false;
                    }
                }
            }

            // prefer enemy if present
            if (bestEnemy != null)
            {
                // Veto if a teammate OR a prop is in front of enemy.
                if (IsOccluded(me, bestEnemy, bestEnemyDepth, dirNorm))
                {
                    return false;
                }
                else
                {
                    target = bestEnemy;
                    isAlly = false;
                    return true;
                }

            }

            else
            {
                // otherwise choose ally only when teamTriggerEnabled
                if (_teamTriggerEnabled && bestAlly != null)
                {
                    target = bestAlly;
                    isAlly = true;
                    return true;
                }
            }

            return false;
        }

        // ------------------ Firing ------------------
        private void StartHold()
        {
            if (IsMouseDown()) return;
            Utility.MouseLeftDown();
            _holding = true;
            _weInitiated = true;
            _holdStartMs = NowMs;
            _holdUntilMs = _holdStartMs + HoldMsBase + _rnd.Next(-20, 25);
        }

        private void StopHold(bool applyCooldown)
        {
            if (_weInitiated) Utility.MouseLeftUp();
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