using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Features;
using CS2Cheat.Utils;
using CS2Cheat.Utils.CFGManager;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Xml.Linq;

namespace CS2Cheat.Graphics;

public class ModernGraphics : ThreadedServiceBase
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;
    private IWindow? _window;
    private GL? _gl;
    private SKSurface? _surface;
    private SKCanvas? _canvas;
    private SKPaint? _paint;
    private SKTypeface? _defaultFont;
    private SKTypeface? _undefeatedFont;
    private SKTypeface? _emojiFont;

    private GRGlInterface? _grInterface;
    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;

    private readonly object _renderLock = new();
    private volatile ConfigManager _config = ConfigManager.Load();
    private bool _overlayVisible = true;
    private bool _lastF11 = false;
    private DateTime _autoHideUntil = DateTime.MinValue;

    // Window styles
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = unchecked((int)0x08000000);
    private const uint LWA_ALPHA = 0x02;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public ModernGraphics(GameProcess gameProcess, GameData gameData)
    {
        _gameProcess = gameProcess ?? throw new ArgumentNullException(nameof(gameProcess));
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
    }

    protected override string ThreadName => nameof(ModernGraphics);

    public GameProcess GameProcess => _gameProcess;
    public GameData GameData => _gameData;
    public bool IsUndefeatedFontLoaded => _undefeatedFont != null && _undefeatedFont != _defaultFont;
    public bool IsEmojiFontLoaded => _emojiFont != null && _emojiFont != _defaultFont;

    public Vector2 MeasureText(string text, float fontSize = 12, bool useCustomFont = false)
    {
        if (string.IsNullOrEmpty(text)) return Vector2.Zero;

        var typeface = _defaultFont;
        if (ContainsEmoji(text) && _emojiFont != null)
            typeface = _emojiFont;
        else if (useCustomFont && _undefeatedFont != null)
            typeface = _undefeatedFont;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = fontSize,
            Typeface = typeface,
            TextEncoding = SKTextEncoding.Utf16
        };

        float width;
        try
        {
            width = paint.MeasureText(text);
        }
        catch
        {
            width = fontSize * text.Length * 0.5f;
        }

        float ascent;
        float descent;
        try
        {
            var metrics = paint.FontMetrics;
            ascent = Math.Abs(metrics.Ascent);
            descent = Math.Abs(metrics.Descent);
        }
        catch
        {
            ascent = fontSize;
            descent = fontSize * 0.2f;
        }

        var height = ascent + descent;
        return new Vector2(width, height);
    }
    //
    private readonly List<DrawCommand> _drawCommands = new();

    public void AddLine(Vector2 start, Vector2 end, SKColor color)
    {
        lock (_renderLock) _drawCommands.Add(new DrawCommand(DrawCommandType.Line, start, end, color));
    }

    public void AddRectangle(Vector2 topLeft, Vector2 bottomRight, SKColor color, bool filled = false)
    {
        lock (_renderLock) _drawCommands.Add(new DrawCommand(DrawCommandType.Rectangle, topLeft, bottomRight, color, filled));
    }

    public void AddText(string text, float x, float y, SKColor color, float fontSize = 12, bool useCustomFont = false)
    {
        lock (_renderLock) _drawCommands.Add(new DrawCommand(DrawCommandType.Text, text, new Vector2(x, y), color, fontSize, useCustomFont));
    }

    public void AddCircle(float cx, float cy, float radius, SKColor color, DrawCommandType type = DrawCommandType.CircleFilled)
    {
        lock (_renderLock) _drawCommands.Add(new DrawCommand(type, new Vector2(cx, cy), radius, color));
    }

    //
    public void DrawLine(uint color, Vector2 start, Vector2 end) => AddLine(start, end, ConvertColor(color));
    public void DrawRect(float x, float y, float w, float h, uint color) => AddRectangle(new(x, y), new(x + w, y + h), ConvertColor(color), true);
    public void DrawRectOutline(float x, float y, float w, float h, uint color) => AddRectangle(new(x, y), new(x + w, y + h), ConvertColor(color), false);
    public void DrawText(string text, float x, float y, uint color, float fontSize = 16, bool useCustomFont = false) => AddText(text, x, y, ConvertColor(color), fontSize, useCustomFont);
    public void DrawCircle(float cx, float cy, float radius, uint color) => AddCircle(cx, cy, radius, ConvertColor(color), DrawCommandType.Circle);
    public void DrawCircleOutline(float cx, float cy, float radius, uint color) => AddCircle(cx, cy, radius, ConvertColor(color), DrawCommandType.CircleOutline);
    public void DrawCircleFilled(float cx, float cy, float radius, uint color) => AddCircle(cx, cy, radius, ConvertColor(color), DrawCommandType.CircleFilled);

    private static bool ContainsEmoji(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i])) i++;
            if ((cp >= 0x1F300 && cp <= 0x1FAFF) ||
                (cp >= 0x1F600 && cp <= 0x1F64F) ||
                (cp >= 0x2600 && cp <= 0x26FF) ||
                (cp >= 0x2700 && cp <= 0x27BF))
                return true;
        }
        return false;
    }

    private static SKColor ConvertColor(uint color)
    {
        return new SKColor(
            (byte)((color >> 16) & 0xFF),
            (byte)((color >> 8) & 0xFF),
            (byte)(color & 0xFF),
            (byte)((color >> 24) & 0xFF)
        );
    }
    public void SetOverlayVisible(bool visible)
    {
        // Silk.NET exposes Win32 as a nullable tuple of (nint Hwnd, nint HDC, nint HInstance).
        var nativeWin32 = _window?.Native?.Win32;
        if (!nativeWin32.HasValue) return;

        var hwnd = new IntPtr(nativeWin32.Value.Hwnd);
        try
        {
            User32.ShowWindow(hwnd, visible ? 4 : 0);
        }
        catch { /* ignore */ }
    }

    private void ApplyWindowStyles()
    {
        var nativeWin32 = _window?.Native?.Win32;
        if (!nativeWin32.HasValue) return;
        var hwnd = new IntPtr(nativeWin32.Value.Hwnd);
        var exStyle = User32.GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        User32.SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        User32.SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
    }

    public void DrawLineWorld(uint color, Vector3 start, Vector3 end)
    {
        var player = _gameData.Player;
        var matrix = player?.MatrixViewProjectionViewport;
        if (player == null || matrix == null) return;

        var startProj = matrix.Value.Transform2(start);
        var endProj = matrix.Value.Transform2(end);
        if (startProj.Z >= 1 || endProj.Z >= 1) return;

        DrawLine(color, new Vector2(startProj.X, startProj.Y), new Vector2(endProj.X, endProj.Y));
    }

    private void RecreateSurface(int width, int height, uint framebufferId = 0)
    {
        _surface?.Dispose();
        _canvas = null;
        _renderTarget?.Dispose();

        if (_grContext != null)
        {
            var framebufferInfo = new GRGlFramebufferInfo(framebufferId, (uint)GLEnum.Rgba8);
            _renderTarget = new GRBackendRenderTarget(width, height, 0, 8, framebufferInfo);
            _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        }
        else
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(info);
        }
        _canvas = _surface?.Canvas;
    }

    private void EnsureInitialized()
    {
        _window ??= InitializeWindow();
        InitializeFonts();
    }

    private IWindow InitializeWindow()
    {
        var options = WindowOptions.Default;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));
        options.IsVisible = false;                   // show it AFTER styling
        options.Size = new Vector2D<int>(800, 600);
        options.Title = "CS2 Overlay";
        options.WindowBorder = WindowBorder.Hidden;
        options.TopMost = true;
        options.TransparentFramebuffer = true;
        options.VSync = false;

        var window = Window.Create(options);
        var self = this;
        IWindow localWindow = window;

        window.Load += () =>
        {
            var gl = localWindow.CreateOpenGL();
            gl.Disable(GLEnum.DepthTest);            // we want pure 2D overlay
            gl.Disable(GLEnum.ScissorTest);
            gl.ClearColor(0f, 0f, 0f, 0f);
            gl.Enable(GLEnum.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.Viewport(0, 0, (uint)localWindow.Size.X, (uint)localWindow.Size.Y);

            self.ApplyWindowStyles();                // set layered/click-through ONCE

            var grInterface = GRGlInterface.Create();
            if (grInterface == null || !grInterface.Validate())
                throw new InvalidOperationException("GRGlInterface creation failed.");

            var grContext = GRContext.CreateGl(grInterface);
            self._gl = gl;
            self._grInterface = grInterface;
            self._grContext = grContext;

            // Bind correct default FBO for Skia
            uint fb = 0u;
            try { fb = (uint)gl.GetInteger(GLEnum.FramebufferBinding); } catch { fb = 0u; }
            self.RecreateSurface(localWindow.Size.X, localWindow.Size.Y, fb);

            // now show WITHOUT activation so CS2 keeps focus
            var win32 = self._window?.Native?.Win32 ?? localWindow.Native?.Win32;
            if (win32.HasValue)
                User32.ShowWindow(new IntPtr(win32.Value.Hwnd), 4); // SW_SHOWNOACTIVATE
        };

        window.Render += dt =>
        {
            if (self._gl == null || self._surface == null || self._canvas == null) return;

            self._gl.Clear(ClearBufferMask.ColorBufferBit);

            self._canvas.Clear(SKColors.Transparent);
            lock (self._renderLock)
            {
                foreach (var cmd in self._drawCommands)
                    self.ExecuteDrawCommand(cmd);
                self._drawCommands.Clear();
            }

            // Skia -> GL
            self._surface.Flush();
            self._grContext?.Flush();                // ok
                                                     // NO gl.Flush(); let Silk swap
        };

        window.Resize += size =>
        {
            if (self._gl == null) return;
            self._gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);

            uint fb = 0u;
            try { fb = (uint)self._gl.GetInteger(GLEnum.FramebufferBinding); } catch { fb = 0u; }
            self._grContext?.ResetContext();         // avoids stale state -> black frames
            self.RecreateSurface(size.X, size.Y, fb);
        };

        window.Initialize();                         // create GL + HWND
        _window = window;
        return window;
    }

    private void InitializeFonts()
    {
        _defaultFont = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;

        try
        {
            var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts", "undefeated.ttf");
            _undefeatedFont = File.Exists(fontPath) ? SKTypeface.FromFile(fontPath) : _defaultFont;
        }
        catch
        {
            _undefeatedFont = _defaultFont;
        }

        try
        {
            var emojiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts", "emoji.ttf");
            _emojiFont = File.Exists(emojiPath) ? SKTypeface.FromFile(emojiPath) : _defaultFont;
        }
        catch
        {
            _emojiFont = _defaultFont;
        }

        _paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 12,
            Typeface = _defaultFont,
            TextEncoding = SKTextEncoding.Utf16
        };
    }
    private void UpdateWindowPosition()
    {
        if (_window == null) return;
        var gameRect = _gameProcess.WindowRectangleClient;
        if (gameRect.Width <= 0 || gameRect.Height <= 0) return;

        try
        {
            _window.Position = new Vector2D<int>(gameRect.X, gameRect.Y);
            _window.Size = new Vector2D<int>(gameRect.Width, gameRect.Height);

            var nativeWin32 = _window.Native?.Win32;
            IntPtr hwnd = IntPtr.Zero;
            if (nativeWin32.HasValue)
            {
                hwnd = new IntPtr(nativeWin32.Value.Hwnd);
            }
            if (hwnd != IntPtr.Zero)
            {
                var topmost = _gameProcess.IsWindowActive ? HWND_TOPMOST : HWND_NOTOPMOST;
                User32.SetWindowPos(hwnd, topmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            User32.ClipCursor(IntPtr.Zero);
        }
        catch { /* ignore */ }
    }

    private void RenderFrame(bool isValid, bool hasWindow)
    {
        var config = _config; // Используем кэш — не читаем с диска!

        lock (_renderLock)
        {
            _drawCommands.Clear();

            var player = _gameData.Player;
            var entities = _gameData.Entities;

            if (isValid)
            {
                if (config.Esp.BoxExtra.Enabled) BoxExtra.Draw(this);
                if (config.Esp.Radar.Enabled) Radar.Draw(this);
                //BombTimer.Draw(this);
                if (config.HitSound.Enabled)
                {
                    HitSound.Process(this);
                    HitSound.DrawHitTexts(this);
                }
            }
            else if (hasWindow)
            {
                AddText("Waiting for game data...", 5, 40, SKColors.Orange, 14);
            }
            else
            {
                AddText("Waiting for Counter-Strike 2 window...", 5, 40, SKColors.Orange, 14);
            }
        }
    }

    protected override void FrameAction()
    {
        EnsureInitialized();
        _window?.DoEvents();

        // Re-apply layered/transparent styles each frame to avoid state changes
        // when the window is clicked or activated by the user (this can cause
        // the layered/transparent flags to be lost on some Windows setups).
        try { ApplyWindowStyles(); } catch { }

        // If GL or Skia surface was lost (black screen after activation/click),
        // try to recreate it using the current framebuffer binding.
        if (_gl == null || _surface == null || _canvas == null)
        {
            try
            {
                if (_window != null)
                {
                    uint fb = 0;
                    try { fb = (uint)(_gl?.GetInteger(GLEnum.FramebufferBinding) ?? 0); } catch { fb = 0; }
                    RecreateSurface(_window.Size.X, _window.Size.Y, fb);
                }
            }
            catch { /* ignore recreation failures for now */ }
        }

        if (_gameProcess.HasWindow)
            UpdateWindowPosition();

        if (_autoHideUntil <= DateTime.UtcNow && !_overlayVisible)
        {
            SetOverlayVisible(true);
            _overlayVisible = true;
        }

        RenderFrame(_gameProcess.IsValid, _gameProcess.HasWindow);
        _window?.DoRender();
    }

    private void ExecuteDrawCommand(DrawCommand command)
    {
        if (_canvas == null || _paint == null) return;

        _paint.Color = command.Color;

        switch (command.Type)
        {
            case DrawCommandType.Circle:
            case DrawCommandType.CircleFilled:
            case DrawCommandType.CircleOutline:
                using (var path = new SKPath())
                {
                    path.AddCircle(command.Start.X, command.Start.Y, command.Radius);
                    _paint.Style = command.Type == DrawCommandType.CircleFilled ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
                    _paint.StrokeWidth = command.Type == DrawCommandType.CircleOutline ? 1f : 2f;
                    _canvas.DrawPath(path, _paint);
                }
                break;

            case DrawCommandType.Line:
                _canvas.DrawLine(command.Start.X, command.Start.Y, command.End.X, command.End.Y, _paint);
                break;

            case DrawCommandType.Rectangle:
                var rect = SKRect.Create(command.Start.X, command.Start.Y,
                    command.End.X - command.Start.X, command.End.Y - command.Start.Y);
                _paint.Style = command.Filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
                _canvas.DrawRect(rect, _paint);
                break;

            case DrawCommandType.Text:
                _paint.Style = SKPaintStyle.Fill;
                _paint.TextSize = command.FontSize;
                _paint.TextEncoding = SKTextEncoding.Utf16;
                _paint.TextAlign = SKTextAlign.Left;

                // Prefer the custom font when requested
                if (command.UseCustomFont && _undefeatedFont != null)
                    _paint.Typeface = _undefeatedFont;
                else if (ContainsEmoji(command.Text) && _emojiFont != null)
                    _paint.Typeface = _emojiFont;
                else
                    _paint.Typeface = _defaultFont;

                float x = Math.Max(0, command.Start.X);
                float y = Math.Max(0, command.Start.Y);
                _canvas.DrawText(command.Text, x, y, _paint);
                break;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _paint?.Dispose();
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
        _grInterface?.Dispose();
        _defaultFont?.Dispose();
        _undefeatedFont?.Dispose();
        _emojiFont?.Dispose();
        _window?.Dispose();
        _gl?.Dispose();
    }

    // === ВСПОМОГАТЕЛЬНЫЕ ТИПЫ ===
    public enum DrawCommandType
    {
        Line,
        Rectangle,
        Text,
        Circle,
        CircleFilled,
        CircleOutline
    }

    public readonly struct DrawCommand
    {
        public readonly DrawCommandType Type;
        public readonly Vector2 Start;
        public readonly Vector2 End;
        public readonly SKColor Color;
        public readonly string Text;
        public readonly float FontSize;
        public readonly bool UseCustomFont;
        public readonly bool Filled;
        public readonly float Radius;

        public DrawCommand(DrawCommandType type, Vector2 start, Vector2 end, SKColor color, bool filled = false)
        {
            Type = type;
            Start = start;
            End = end;
            Color = color;
            Text = string.Empty;
            FontSize = 0;
            UseCustomFont = false;
            Filled = filled;
            Radius = 0;
        }

        public DrawCommand(DrawCommandType type, string text, Vector2 pos, SKColor color, float fontSize, bool useCustomFont)
        {
            Type = type;
            Start = pos;
            End = Vector2.Zero;
            Color = color;
            Text = text;
            FontSize = fontSize;
            UseCustomFont = useCustomFont;
            Filled = false;
            Radius = 0;
        }

        public DrawCommand(DrawCommandType type, Vector2 center, float radius, SKColor color)
        {
            Type = type;
            Start = center;
            End = Vector2.Zero;
            Color = color;
            Text = string.Empty;
            FontSize = 0;
            UseCustomFont = false;
            Filled = type == DrawCommandType.CircleFilled;
            Radius = radius;
        }
    }
}