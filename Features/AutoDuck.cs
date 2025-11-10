using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using WindowsInput;
using WindowsInput.Native;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features
{
    public class AutoDuck : ThreadedServiceBase
    {
        protected override string ThreadName => nameof(AutoDuck);

        private readonly InputSimulator _input = new InputSimulator();
        private GameProcess GameProcess { get; }
        private GameData GameData { get; }

        private static readonly Keys DuckToggleKey = Keys.OemQuestion; // '?'
        private bool _isEnabled = true;
        private bool _toggleLatch = false;

        private bool _ctrlHeld = false;

        public AutoDuck(GameProcess gameProcess, GameData gameData)
        {
            GameProcess = gameProcess;
            GameData = gameData;
        }

        protected override void FrameAction()
        {
            // Toggle on/off
            if (DuckToggleKey.IsKeyDown() && !_toggleLatch)
            {
                _isEnabled = !_isEnabled;
                _toggleLatch = true;
                Console.WriteLine($"[+]AutoDuck: {(_isEnabled ? "ON" : "OFF")}");
            }
            else if (!DuckToggleKey.IsKeyDown() && _toggleLatch)
            {
                _toggleLatch = false;
            }

            if (!_isEnabled) { EnsureCtrlReleased(); return; }
            if (!GameProcess.IsValid || !GameData.Player.IsAlive()) { EnsureCtrlReleased(); return; }

            // Simple rule: while Space is held, hold CTRL; release when Space is up
            bool spaceDown = Keys.Space.IsKeyDown();

            if (spaceDown && !_ctrlHeld)
            {
                _input.Keyboard.KeyDown(VirtualKeyCode.CONTROL);
                _ctrlHeld = true;
            }
            else if (!spaceDown && _ctrlHeld)
            {
                _input.Keyboard.KeyUp(VirtualKeyCode.CONTROL);
                _ctrlHeld = false;
            }
        }

        private void EnsureCtrlReleased()
        {
            if (_ctrlHeld)
            {
                _input.Keyboard.KeyUp(VirtualKeyCode.CONTROL);
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
