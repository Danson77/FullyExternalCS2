using CS2Cheat.Utils;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CS2Cheat.Features
{
    public class AntiFlash : ThreadedServiceBase
    {
        private readonly Graphics.Graphics _graphics;
        private Form _overlayForm;
        private bool _hasFlashed = false;  // Track whether the player has been flashed
        private System.Threading.Timer _flashTimer; // Specify System.Threading.Timer to avoid ambiguity
        private System.Threading.Timer _fadeTimer;  // New timer for fading the tint
        private const int FlashThreshold = 1080200000; // Threshold above which the player is considered flashed
        private const double FadeDuration = 1000;  // Fade duration in milliseconds (3.5 seconds)
        private double _currentOpacity = 0.9;  // Starting opacity
        private int _fadeStepInterval = 10;  // Time interval in ms between each fade step
        private double _fadeStepAmount;  // Amount to decrease opacity per step

        public AntiFlash(Graphics.Graphics graphics)
        {
            _graphics = graphics;
            InitializeOverlay();
            _flashTimer = new System.Threading.Timer(HideTintAfterDelay, null, Timeout.Infinite, Timeout.Infinite); // Initialize timer with no immediate action
            _fadeStepAmount = 0.9 / (FadeDuration / _fadeStepInterval);  // Calculate how much to reduce opacity every step
        }

        private void InitializeOverlay()
        {
            _overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                TopMost = true,
                Width = 2560,
                Height = 1440,
                BackColor = System.Drawing.Color.Black,
                Opacity = 0.9,
                ShowInTaskbar = false,
                Visible = false // Start with the overlay hidden
            };

            // Allow clicks to pass through the overlay
            SetWindowLong(_overlayForm.Handle, -20, (IntPtr)(GetWindowLong(_overlayForm.Handle, -20).ToInt32() | 0x20 | 0x80000));

            // Check if the overlay form was initialized correctly
            if (_overlayForm != null)
            {
                // Overlay starts hidden, no need to show it during initialization
            }
            else
            {
                Console.WriteLine("Failed to initialize overlay form.");
            }
        }

        protected override string ThreadName => nameof(AntiFlash);

        protected override void FrameAction()
        {
            ApplyFlashProtection(_graphics);  // Run flash protection logic in each frame
        }

        public void ApplyFlashProtection(Graphics.Graphics graphics)
        {
            // Check if the player is flashed based on the flash threshold
            if (graphics.GameData.Player.FlashAlpha >= FlashThreshold)
            {
                // Player is flashed, show overlay if not already done
                if (!_hasFlashed)
                {
                    Console.WriteLine("-Player is flashed! Applying flash protection...");
                    _hasFlashed = true; // Set the flag to prevent repeated messages
                    ShowTint();  // Show the overlay
                    _flashTimer.Change(3200, Timeout.Infinite); // Start timer to hide tint after 1 second
                }
            }
            else
            {
                // Player is not flashed, hide overlay if it was previously shown
                if (_hasFlashed)
                {
                    //Console.WriteLine("Player not flashed");
                    _hasFlashed = false; // Reset the flag to allow new flash detection
                    _flashTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer if flash is reset
                    StartFadeOut();  // Start fading the tint out
                }
            }
        }

        public void ShowTint()
        {
            try
            {
                // Ensure the form visibility change happens on the UI thread
                if (_overlayForm.InvokeRequired)
                {
                    _overlayForm.Invoke(new Action(ShowTint));
                }
                else
                {
                    //Console.WriteLine("Showing overlay");
                    _overlayForm.Visible = true;  // Show the overlay when the player is flashed
                    _currentOpacity = 0.9;  // Reset opacity to 0.9 when showing tint
                    _overlayForm.Opacity = _currentOpacity;  // Set the initial opacity
                    // We can also start the fade in process if you want to smoothen that.
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in ShowTint: " + ex.Message);
            }
        }

        public void HideTint()
        {
            try
            {
                // Ensure the form visibility change happens on the UI thread
                if (_overlayForm.InvokeRequired)
                {
                    _overlayForm.Invoke(new Action(HideTint));
                }
                else
                {
                    //Console.WriteLine("Hiding overlay");
                    _overlayForm.Visible = false; // Hide the overlay when the player is no longer flashed
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in HideTint: " + ex.Message);
            }
        }

        // Callback for the timer to hide the tint after 1 second
        private void HideTintAfterDelay(object state)
        {
            StartFadeOut(); // Start fading the tint out after delay
        }

        private void StartFadeOut()
        {
            if (_fadeTimer != null)
            {
                _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop any existing fade out
            }

            // Start a timer to gradually reduce the opacity over time
            _fadeTimer = new System.Threading.Timer(FadeOutStep, null, 0, _fadeStepInterval);
        }

        private void FadeOutStep(object state)
        {
            // Gradually decrease opacity in small steps
            _currentOpacity -= _fadeStepAmount;

            if (_currentOpacity <= 0.0)
            {
                _currentOpacity = 0.0;
                _fadeTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop fading
                HideTint(); // Hide the tint once fully faded
            }

            // Update opacity on the UI thread
            if (_overlayForm.InvokeRequired)
            {
                _overlayForm.Invoke(new Action(() => _overlayForm.Opacity = _currentOpacity));
            }
            else
            {
                _overlayForm.Opacity = _currentOpacity;
            }
        }

        // PInvoke to make the window transparent to clicks
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hwnd, int nIndex, IntPtr dwNewLong);
    }
}
