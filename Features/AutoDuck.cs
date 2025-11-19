using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using WindowsInput;
using WindowsInput.Native;
using Keys = Process.NET.Native.Types.Keys;
using System.Runtime.InteropServices;

namespace CS2Cheat.Features
{
    public class AutoDuck : ThreadedServiceBase
    {
        protected override string ThreadName => nameof(AutoDuck);

        private readonly InputSimulator _input = new InputSimulator();
        private GameProcess GameProcess { get; }
        private GameData GameData { get; }

        // Separate toggles
        private static readonly Keys SwitchToggleKey = Keys.OemQuestion; // '?'

        private bool _switchEnabled = true;
        private bool _switchToggleLatch = false;

        private bool _ctrlHeld = false;
        private bool _spaceHeld = false;

        // Win32 to check key state directly (more reliable for spacebar)
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_SPACE = 0x20;

        public AutoDuck(GameProcess gameProcess, GameData gameData)
        {
            GameProcess = gameProcess;
            GameData = gameData;
        }

        protected override void FrameAction()
        {
            // ==========================
            // Handle toggles
            // ==========================

            // AutoDuck toggle ( ? )
            if (SwitchToggleKey.IsKeyDown() && !_switchToggleLatch)
            {
                _switchEnabled = !_switchEnabled;
                _switchToggleLatch = true;
                Console.WriteLine($"[+] AutoDuck or BunnyHop: {(_switchEnabled ? "AutoDuck" : "BunnyHop")}");
            }
            else if (!SwitchToggleKey.IsKeyDown() && _switchToggleLatch)
            {
                _switchToggleLatch = false;
            }

            // ==========================
            // Sanity checks
            // ==========================

            if (!GameProcess.IsValid || !GameData.Player.IsAlive())
            {
                EnsureCtrlReleased();
                return;
            }

            bool spaceDown = (GetAsyncKeyState(VK_SPACE) & 0x8000) != 0;
            _spaceHeld = spaceDown;

            // ==========================
            // AUTO-DUCK WHILE HOLDING SPACE
            // ==========================
            if (_switchEnabled)
            {
                if (spaceDown && !_ctrlHeld)
                {
                    _input.Keyboard.KeyDown(VirtualKeyCode.LCONTROL);
                    _ctrlHeld = true;
                }
                else if (!spaceDown && _ctrlHeld)
                {
                    _input.Keyboard.KeyUp(VirtualKeyCode.LCONTROL);
                    _ctrlHeld = false;
                }
            }
            else
            {
                // Duck feature disabled -> make sure we don't keep CTRL stuck
                if (_ctrlHeld)
                {
                    _input.Keyboard.KeyUp(VirtualKeyCode.LCONTROL);
                    _ctrlHeld = false;
                }
            }

            // ==========================
            // BUNNY HOP LOGIC
            // ==========================
            // Only works while holding space, and player is on ground
            if (!_switchEnabled && spaceDown && IsOnGround())
            {
                _input.Keyboard.KeyDown(VirtualKeyCode.SPACE);
                Thread.Sleep(10);
                _input.Keyboard.KeyUp(VirtualKeyCode.SPACE);
            }
        }

        private bool IsOnGround()
        {
            try
            {
                // Check m_fFlags from player entity (bitmask)
                // 1 << 0 = on ground (0x1)
                int flags = GameProcess.Process.Read<int>(GameData.Player.AddressBase + Offsets.m_fFlags);
                return (flags & 1) != 0;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureCtrlReleased()
        {
            if (_ctrlHeld)
            {
                _input.Keyboard.KeyUp(VirtualKeyCode.LCONTROL);
                _ctrlHeld = false;
            }
        }

        public override void Dispose()
        {
            EnsureCtrlReleased();
            base.Dispose();
        }
    }
}
