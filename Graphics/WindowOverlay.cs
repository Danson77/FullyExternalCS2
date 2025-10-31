using System;
using System.Windows.Threading;
using CS2Cheat.Data.Game;
using CS2Cheat.Utils;
using GameOverlay.Windows;
using SharpDX;
using static System.Windows.Application;
using Color = SharpDX.Color;
using Rectangle = System.Drawing.Rectangle;

namespace CS2Cheat.Graphics
{
    public class WindowOverlay : ThreadedServiceBase
    {
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private TimeSpan _updateInterval = TimeSpan.FromMilliseconds(100); // Update interval (100ms = 10fps)

        public WindowOverlay(GameProcess gameProcess)
        {
            GameProcess = gameProcess;

            Window = new OverlayWindow
            {
                Title = "Overlay",
                IsTopmost = true,
                IsVisible = true,
                X = -32000,
                Y = -32000,
                Width = 16,
                Height = 16
            };

            Window.Create();
        }

        protected override string ThreadName => nameof(WindowOverlay);

        private GameProcess GameProcess { get; set; }

        public OverlayWindow Window { get; private set; }

        public override void Dispose()
        {
            base.Dispose();

            Window.Dispose();
            Window = default;

            GameProcess = default;
        }

        protected override void FrameAction()
        {
            Update(GameProcess.WindowRectangleClient);
        }

        private void Update(Rectangle windowRectangle)
        {
            var now = DateTime.Now;
            if (now - _lastUpdateTime >= _updateInterval)  // Throttle the update rate
            {
                _lastUpdateTime = now;

                // Only invoke updates when necessary
                if (Window.X != windowRectangle.Location.X || Window.Y != windowRectangle.Location.Y ||
                    Window.Width != windowRectangle.Size.Width || Window.Height != windowRectangle.Size.Height)
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        if (windowRectangle is { Width: > 0, Height: > 0 })
                        {
                            Window.X = windowRectangle.Location.X;
                            Window.Y = windowRectangle.Location.Y;
                            Window.Width = windowRectangle.Size.Width;
                            Window.Height = windowRectangle.Size.Height;
                        }
                        else
                        {
                            Window.X = -32000;
                            Window.Y = -32000;
                            Window.Width = 16;
                            Window.Height = 16;
                        }
                    }, DispatcherPriority.Normal);
                }
            }
        }

        public static void Draw(GameProcess gameProcess, Graphics graphics)
        {
        }
    }
}
