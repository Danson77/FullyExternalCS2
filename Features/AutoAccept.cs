using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using WindowsInput.Native;
using WindowsInput;
using System;
using System.Drawing;


namespace CS2Cheat.Features
{
    public class AutoAccept : ThreadedServiceBase
    {
        protected override string ThreadName => nameof(AutoAccept);
        private GameProcess GameProcess { get; set; }
        private GameData GameData { get; set; }

        private bool isAutoAcceptEnabled = true;
        private InputSimulator inputSimulator;

        public AutoAccept(GameProcess gameProcess, GameData gameData)
        {
            GameProcess = gameProcess;
            GameData = gameData;
            inputSimulator = new InputSimulator();
        }

        // For 2K resolution (2560x1440)
        private const double centerX = 1920;
        private const double offsetY = 40; // Fixed offset above center
        private const double centerY = 1080 - offsetY;

        private DateTime lastActionTime = DateTime.MinValue; // Store last action timestamp

        protected override void FrameAction()
        {
            if (!isAutoAcceptEnabled || !GameProcess.IsValid) return;

            // Ensure the action runs only once every 20 seconds
            if ((DateTime.Now - lastActionTime).TotalSeconds < 20)
            {
                return;
            }

            // Get the color at the specified point
            Color colorAtPoint = GetColorAtPoint();

            // Define the color range (green and darker green)
            Color Bound1 = Color.FromArgb(25, 113, 29);
            Color Bound2 = Color.FromArgb(26, 114, 30);
            Color Bound3 = Color.FromArgb(28, 115, 32);
            Color Bound4 = Color.FromArgb(29, 117, 32);
            Color Bound5 = Color.FromArgb(29, 117, 33);
            Color Bound6 = Color.FromArgb(37, 140, 40);

            // Check if the captured color is within any of the defined ranges
            if (IsColorInAnyRange(colorAtPoint, Bound1, Bound2, Bound3, Bound4, Bound5, Bound6))
            {
                TriggerAction();
                //Console.WriteLine("[+] Color is within range! Accepting in 1 second...");
                lastActionTime = DateTime.Now; // Update last execution timestamp
            }
        }

        private void MoveMouse()
        {
            // Move the mouse to the center with offset
            double normalizedX = (centerX / 2560) * 65535.0; // 2560 is the width for 2K resolution
            double normalizedY = (centerY / 1440) * 65535.0; // 1440 is the height for 2K resolution

            // Move the mouse
            inputSimulator.Mouse.MoveMouseToPositionOnVirtualDesktop(normalizedX, normalizedY);
        }

        // Function to capture a part of the screen and check the color
        private Color GetColorAtPoint()
        {
            // Get the screen resolution dynamically
            int screenWidth = Screen.PrimaryScreen.Bounds.Width;
            int screenHeight = Screen.PrimaryScreen.Bounds.Height;

            // Calculate coordinates from bottom-left:
            int x = screenWidth / 2;   // Center horizontally
            int y = (screenHeight / 2);  // Halfway up from the bottom

            Thread.Sleep(100);

            // Create a Bitmap object to hold the screenshot
            using (Bitmap screenshot = new Bitmap(1, 1))
            {
                // Capture the screen at (x, y)
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(screenshot))
                {
                    g.CopyFromScreen(x, (y + 40), 0, 0, new Size(1, 1));
                }

                // Get the color of the pixel at (0, 0)
                return screenshot.GetPixel(0, 0);
            }
        }

        // Function to check if the color is within a range
        private bool IsColorInRange(Color capturedColor, Color color1, Color color2)
        {
            // Check if each component (Red, Green, Blue) is within the range
            bool isRedInRange = capturedColor.R >= Math.Min(color1.R, color2.R) && capturedColor.R <= Math.Max(color1.R, color2.R);
            bool isGreenInRange = capturedColor.G >= Math.Min(color1.G, color2.G) && capturedColor.G <= Math.Max(color1.G, color2.G);
            bool isBlueInRange = capturedColor.B >= Math.Min(color1.B, color2.B) && capturedColor.B <= Math.Max(color1.B, color2.B);

            return isRedInRange && isGreenInRange && isBlueInRange;
        }

        // Function to check if the color is within any of the given ranges
        private bool IsColorInAnyRange(Color capturedColor, params Color[] bounds)
        {
            foreach (var bound in bounds)
            {
                // Check if the captured color is within this range
                if (IsColorInRange(capturedColor, bound, bound))
                {
                    return true;
                }
            }
            return false;
        }

        // Function to perform the action (e.g., clicking a button)
        private void TriggerAction()
        {
            MoveMouse();
            inputSimulator.Mouse.LeftButtonClick();
            Console.WriteLine("[+] Match Accepted!");
        }
    }
}
