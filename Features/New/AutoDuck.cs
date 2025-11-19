//using CS2Cheat.Core;
//using CS2Cheat.Data.Game;
//using CS2Cheat.Utils;
//
//using WindowsInput.Native;
//using WindowsInput;
//
//using Keys = Process.NET.Native.Types.Keys;
//using System.Diagnostics;
//using CS2Cheat.Core.Data;
//
//using SharpDX;
//using CS2Cheat.Graphics;
//
//namespace CS2Cheat.Features
//{
//    public class AutoDuck : ThreadedServiceBase
//    {
//        protected override string ThreadName => nameof(AutoDuck);
//        private InputSimulator inputSimulator;
//
//        public Vector3 EyePosition { get; set; }
//        public Vector3 AimDirection { get; set; }
//        private GameProcess GameProcess { get; set; }
//        private GameData GameData { get; set; }
//
//        private Stopwatch fireTimer = new Stopwatch(); // Timer for when shooting
//        private Stopwatch fireStopwatch = new Stopwatch(); // Timer to track time since last shot
//        private int lastShotsFired = 0;
//
//        private long spacePressTime = 0; // Time when space is pressed
//
//        private int lastHealth;
//
//        private static Keys DuckToggleKey = Keys.OemQuestion; // Toggle key for auto-ducking
//
//        private bool isAutoDuckEnabled = false;
//        private bool isSpacePressed = false;
//        private bool isDuckToggled = false;
//        private bool isCrouching = false; // Track if currently crouching
//        private bool hasMessageBeenSent = false; // Track if the message has been displayed
//
//        private Stopwatch hitCooldownTimer = new Stopwatch(); // Cooldown timer for hit detection
//        private const int HitCooldown = 500; // 2-second cooldown to prevent repeated crouching
//
//        public AutoDuck(GameProcess gameProcess, GameData gameData)
//        {
//            GameProcess = gameProcess;
//            GameData = gameData;
//            inputSimulator = new InputSimulator(); // Initialize the InputSimulator
//        }
//
//        // Define allowed weapon names
//        private static readonly HashSet<string> AllowedWeapons = new HashSet<string>
//        {
//            //nameof(WeaponIndexes.Awp),
//            //nameof(WeaponIndexes.Ssg08),
//            //nameof(WeaponIndexes.Deagle),
//            //nameof(WeaponIndexes.Revolver)
//        };
//
//        protected override void FrameAction()
//        {
//            // Handle toggle state for AutoDuck
//            if (DuckToggleKey.IsKeyDown() && !isDuckToggled)
//            {
//                isAutoDuckEnabled = !isAutoDuckEnabled;
//                Console.WriteLine($"[+]AutoDuck Enabled: {isAutoDuckEnabled}");
//                isDuckToggled = true;
//            }
//            else if (!DuckToggleKey.IsKeyDown() && isDuckToggled)
//            {
//                isDuckToggled = false;
//            }
//
//            if (!isAutoDuckEnabled || !GameProcess.IsValid || !GameData.Player.IsAlive()) return;
//
//            // Trigger crouch if an enemy is aiming at us and has fired (whether hit or missed)
//            if (IsEnemyAimingAtMe() && !hitCooldownTimer.IsRunning)
//            {
//                Console.WriteLine("-Enemy is firing at us! Ducking...");
//                HoldCtrlKey();
//                Thread.Sleep(600);  // Hold crouch for 0.6 seconds
//                ReleaseCtrlKey();
//                hitCooldownTimer.Restart();  // Reset cooldown timer after crouching
//            }
//
//            // Adjust hit detection logic (if you're hit, trigger crouch)
//            if (GameData.Player.Health < lastHealth)
//            {
//                if (!hitCooldownTimer.IsRunning || hitCooldownTimer.ElapsedMilliseconds > HitCooldown)
//                {
//                    Console.WriteLine("-Hit detected! Ducking...");
//                    HoldCtrlKey();
//                    Thread.Sleep(600);  // Hold crouch for 0.6 seconds after hit
//                    ReleaseCtrlKey();
//                    hitCooldownTimer.Restart();
//                }
//            }
//
//            // Auto-Duck when Firing (Only for Allowed Weapons)**
//            if (IsFiring() && IsUsingAllowedWeapon())
//            {
//                fireTimer.Restart();
//                isCrouching = true;
//            }
//
//            if (fireTimer.ElapsedMilliseconds > 0 && fireTimer.ElapsedMilliseconds <= 1400)
//            {
//                HoldCtrlKey();
//            }
//
//            else if (fireTimer.ElapsedMilliseconds > 1400)
//            {
//                ReleaseCtrlKey();
//                fireTimer.Reset();
//                isCrouching = false;
//            }
//
//            // Stand up only if no shots fired for at least 1.4 seconds
//            if (isCrouching && fireStopwatch.ElapsedMilliseconds >= 1400)
//            {
//                Console.WriteLine("-Stopped firing, standing up...");
//                ReleaseCtrlKey();
//                isCrouching = false;
//            }
//
//            // Handle the jump action: Check if Space is being held
//            if (Keys.Space.IsKeyDown())
//            {
//                if (!isSpacePressed)
//                {
//                    spacePressTime = DateTime.Now.Ticks; // Record the time when space is pressed
//                    isSpacePressed = true;
//                }
//
//                if (DateTime.Now.Ticks >= TimeSpan.TicksPerSecond * 0.5) // If space is held for more than 0.x seconds, perform the jump action
//                {
//                    if (!hasMessageBeenSent)
//                    {
//                        hasMessageBeenSent = true; // Prevent message from printing again until space is released
//                    }
//                    HoldCtrlKey();
//                }
//            }
//            else
//            {
//                if (isSpacePressed) // If space was released, reset and release Ctrl
//                {
//                    isSpacePressed = false;
//                    ReleaseCtrlKey(); // Release Left Ctrl after space is released
//                    hasMessageBeenSent = false; // Reset message flag
//                }
//            }
//        }
//
//        // Method to check if the player fired a shot
//        private bool IsFiring()
//        {
//            int currentShotsFired = GameData.Player.ShotsFired;
//            bool hasFired = currentShotsFired > lastShotsFired; // Detect new shot
//            lastShotsFired = currentShotsFired; // Update shot count
//            return hasFired;
//        }
//
//        // Simulate holding the Left Ctrl key using InputSimulator
//        private void HoldCtrlKey()
//        {
//            inputSimulator.Keyboard.KeyDown(VirtualKeyCode.CONTROL);
//        }
//
//        // Simulate releasing the Left Ctrl key using InputSimulator
//        private void ReleaseCtrlKey()
//        {
//            inputSimulator.Keyboard.KeyUp(VirtualKeyCode.CONTROL);
//        }
//
//        // Check if Using Allowed Weapon
//        private bool IsUsingAllowedWeapon()
//        {
//            return AllowedWeapons.Contains(GameData.Player.CurrentWeaponName);
//        }
//
//        public static class AimHelper
//        {
//            /// <summary>
//            /// Converts pitch and yaw angles (in degrees) into a normalized directional vector.
//            /// </summary>
//            public static Vector3 GetAimDirection(float pitch, float yaw)
//            {
//                // Convert degrees to radians
//                float radPitch = MathUtil.DegreesToRadians(pitch);
//                float radYaw = MathUtil.DegreesToRadians(yaw);
//
//                // Calculate the direction vector components
//                float x = (float)(Math.Cos(radPitch) * Math.Cos(radYaw));
//                float y = (float)(Math.Cos(radPitch) * Math.Sin(radYaw));
//                float z = (float)(-Math.Sin(radPitch)); // Adjust the sign if necessary for your coordinate system.
//
//                return new Vector3(x, y, z);
//            }
//        }
//
//        // Helper function to check if the entity is a valid player
//        private bool IsValidPlayer(IntPtr entity)
//        {
//            if (entity == IntPtr.Zero) return false;
//
//            // Check if entity has health (non-players typically have 0 or invalid health)
//            int health = GameProcess.Process.Read<int>(entity + Offsets.m_iHealth);
//            if (health <= 0 || health > 100) return false; // Players have health between 1-100
//
//            // Check if entity has a valid team number (usually 2 = T, 3 = CT)
//            int teamNum = GameProcess.Process.Read<int>(entity + Offsets.m_iTeamNum);
//            if (teamNum != 2 && teamNum != 3) return false; // Non-players typically don't have a valid team
//
//            return true;
//        }
//
//        // Check if any enemy is aiming at the player
//        private bool IsEnemyAimingAtMe()
//        {
//            // Loop through all entities in the game data.
//            foreach (var enemy in GameData.Entities)
//            {
//                // Get local player entity ID
//                var entityId = GameProcess.Process.Read<int>(GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwLocalPlayerPawn) + Offsets.m_iIDEntIndex);
//
//                // Read the target entity
//                var entityEntry = GameProcess.Process.Read<IntPtr>(GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwEntityList) + 0x8 * (entityId >> 9) + 0x10);
//                var entity = GameProcess.Process.Read<IntPtr>(entityEntry + 120 * (entityId & 0x1FF));
//
//                // Get the target's eye position
//                Vector3 targetEyePosition = GameProcess.Process.Read<Vector3>(entity + Offsets.m_vecViewOffset);
//
//                // Compute the direction vector from the enemy's eyes to our eyes.
//                Vector3 toPlayer = GameData.Player.EyePosition - targetEyePosition;
//                toPlayer = toPlayer.GetNormalized(); // Normalize the vector
//
//                // Retrieve the enemy's eye angles (Pitch, Yaw)
//                var eyeAngles = GameProcess.Process.Read<Vector2>(entity + Offsets.m_angEyeAngles);
//
//                // Calculate the aim direction from the eye angles.
//                Vector3 enemyAimDir = AimHelper.GetAimDirection(eyeAngles.X, eyeAngles.Y);
//
//                // Read the enemy team
//                var entityTeam = GameProcess.Process.Read<int>(entity + Offsets.m_iTeamNum);
//
//                // Read the number of shots fired by the enemy (using Offsets.m_iShotsFired)
//                int shotsFiredByEnemy = GameProcess.Process.Read<int>(entity + Offsets.m_iShotsFired);
//
//                //// Validate entity before proceeding
//                if (entity == IntPtr.Zero || !IsValidPlayer(entity))
//                    continue;
//
//
//                // Only consider alive enemies on the opposing team.
//                if (!enemy.IsAlive() || enemy.Team == GameData.Player.Team)
//                    continue;
//                
//                // Only trigger if enemy has fired recently
//                //if (shotsFiredByEnemy > 0)
//                //    continue;
//
//                // Calculate the dot product between the enemy's aim direction and the direction to our player.
//                float dot = Vector3.Dot(enemyAimDir, toPlayer);
//
//                // A dot product near 1 means the enemy is looking directly at us.
//                // No restriction to specific body parts, just if the enemy is looking at us.
//                if (dot > 0.20f)
//                {
//                    return true; // Enemy is aiming at us
//                }
//            }
//            return false; // No enemy is aiming at us
//        }
//
//    }
//}
