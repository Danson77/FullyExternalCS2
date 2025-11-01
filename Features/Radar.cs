using System.Numerics;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Graphics;
using CS2Cheat.Utils.CFGManager;
using static CS2Cheat.Core.User32;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

public static class Radar
{
    private const float UnitsToMeters = 0.0254f;

    // --- Team-ESP toggle state ---
    private static bool TeamVisible = false;
    private static bool isTeamVisibleToggled = false;

    // Pick a default toggle key (')  — replace with a config key if you have one
    private static readonly Keys toggleTeamVisibleKey = Keys.OemSemicolon;

    // Tiny helper, same as your other features
    private static bool IsKeyDown(this Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    private static void HandleToggles()
    {
        if (toggleTeamVisibleKey.IsKeyDown() && !isTeamVisibleToggled)
        {
            TeamVisible = !TeamVisible;
            isTeamVisibleToggled = true;
        }
        else if (!toggleTeamVisibleKey.IsKeyDown() && isTeamVisibleToggled)
        {
            isTeamVisibleToggled = false;
        }
    }

    private static uint ToUintColor(SkiaSharp.SKColor color)
    {
        return ((uint)color.Alpha << 24) |
               ((uint)color.Red << 16) |
               ((uint)color.Green << 8) |
               color.Blue;
    }
    public static void Draw(ModernGraphics graphics)
    {
        // Handle toggle each frame
        HandleToggles();

        var cfg = ConfigManager.Load();
        var radarCfg = cfg.Esp.Radar;
        if (!radarCfg.Enabled) return;

        // --- bail if game/window not valid (menu/lobby/minimized) ---
        var gp = graphics.GameProcess;
        if (!gp.IsValid || !gp.HasWindow) return;

        var player = graphics.GameData.Player;
        var entities = graphics.GameData.Entities;
        if (player == null || entities == null) return;

        // Snapshot – if we can't get it, we're not in a live round
        RenderSnapshot snapshot;
        lock (player.RenderDataLock)
            snapshot = player.RenderData;
        if (snapshot == null) return;

        // Heuristics for menu/lobby: zero/NaN position, zero matrix, or no entities
        if (snapshot.Position == default ||
            float.IsNaN(snapshot.Position.X) || float.IsNaN(snapshot.Position.Y) || float.IsNaN(snapshot.Position.Z))
            return;

        // Matrix guard (Skips when view isn’t initialized yet)
        if (snapshot.MatrixViewProjection.M11 == 0f && snapshot.MatrixViewProjection.M22 == 0f &&
            snapshot.MatrixViewProjection.M33 == 0f && snapshot.MatrixViewProjection.M44 == 0f)
            return;

        // Optional: if you only want radar during live play, require local alive or at least someone alive
        // if (!player.IsAlive() && entities.TrueForAll(e => !e.IsAlive())) return;

        // ------- FROM HERE: your existing drawing code -------
        float centerX = radarCfg.X + radarCfg.Size / 2f;
        float centerY = radarCfg.Y + radarCfg.Size / 2f;
        float radius = radarCfg.Size / 2f;

        graphics.DrawCircleFilled(centerX, centerY, radius, ToUintColor(SkiaSharp.SKColors.Black.WithAlpha(160)));
        graphics.DrawCircleOutline(centerX, centerY, radius, ToUintColor(SkiaSharp.SKColors.White.WithAlpha(100)));

        var gray = ToUintColor(SkiaSharp.SKColors.Gray.WithAlpha(80));
        graphics.DrawLine(gray, new Vector2(radarCfg.X, centerY), new Vector2(radarCfg.X + radarCfg.Size, centerY));
        graphics.DrawLine(gray, new Vector2(centerX, radarCfg.Y), new Vector2(centerX, radarCfg.Y + radarCfg.Size));

        var labelColor = ToUintColor(SkiaSharp.SKColors.White.WithAlpha(180));
        graphics.DrawText("N", centerX, radarCfg.Y + 10, labelColor, fontSize: 12);
        graphics.DrawText("S", centerX, radarCfg.Y + radarCfg.Size - 15, labelColor, fontSize: 12);
        graphics.DrawText("W", radarCfg.X + 10, centerY, labelColor, fontSize: 12);
        graphics.DrawText("E", radarCfg.X + radarCfg.Size - 15, centerY, labelColor, fontSize: 12);

        float pixelsPerMeter = radius / radarCfg.MaxDistance;

        if (radarCfg.ShowLocalPlayer)
        {
            graphics.DrawCircleFilled(centerX, centerY, 5f, ToUintColor(SkiaSharp.SKColors.Cyan));
            graphics.DrawCircleOutline(centerX, centerY, 5f, ToUintColor(SkiaSharp.SKColors.White));
            graphics.DrawLine(ToUintColor(SkiaSharp.SKColors.White),
                new Vector2(centerX, centerY),
                new Vector2(centerX, centerY - 12f));
        }

        // heading (unchanged)
        var viewMatrix = snapshot.MatrixViewProjection;
        var forwardVector = new Vector2(viewMatrix.M13, viewMatrix.M23);
        float playerYawRad = MathF.Atan2(forwardVector.X, forwardVector.Y);
        float cosYaw = MathF.Cos(playerYawRad);
        float sinYaw = MathF.Sin(playerYawRad);

        foreach (var entity in entities)
        {
            if (!entity.IsAlive() || entity.AddressBase == player.AddressBase) continue;

            bool isTeammate = entity.Team == player.Team;
            if (!TeamVisible && isTeammate) continue;

            float relFwd = entity.Position.X - snapshot.Position.X;
            float relRight = entity.Position.Y - snapshot.Position.Y;

            float distUnits = MathF.Sqrt(relFwd * relFwd + relRight * relRight);
            float distMeters = distUnits * UnitsToMeters;
            if (distMeters > radarCfg.MaxDistance) continue;

            float rx = (relFwd * cosYaw) - (relRight * sinYaw);
            float ry = (relFwd * sinYaw) + (relRight * cosYaw);

            float x = centerX + rx * UnitsToMeters * pixelsPerMeter;
            float y = centerY - ry * UnitsToMeters * pixelsPerMeter;

            string colorHex = (entity.Team == Team.Terrorists) ? radarCfg.EnemyColor : radarCfg.TeamColor;
            byte alpha = entity.IsVisible
                ? Convert.ToByte(radarCfg.VisibleAlpha, 16)
                : Convert.ToByte(radarCfg.InvisibleAlpha, 16);

            uint baseColor = Convert.ToUInt32(colorHex, 16);
            uint playerColor = ((uint)alpha << 24) | (baseColor & 0x00FFFFFFu);

            graphics.DrawCircleFilled(x, y, 3.5f, playerColor);
            graphics.DrawCircleOutline(x, y, 3.5f, ToUintColor(SkiaSharp.SKColors.White.WithAlpha(200)));

            if (radarCfg.ShowDirectionArrow && entity.ViewAngle.HasValue)
            {
                float entityYawRad = (entity.ViewAngle.Value.Y + 90f) * MathF.PI / 180f;
                float arrowYawRad = entityYawRad - playerYawRad;

                float ax = x + MathF.Sin(arrowYawRad) * 7f;
                float ay = y - MathF.Cos(arrowYawRad) * 7f;

                uint arrowColor = entity.IsVisible
                    ? ToUintColor(SkiaSharp.SKColors.White)
                    : ToUintColor(SkiaSharp.SKColors.Gray);

                graphics.DrawLine(arrowColor, new Vector2(x, y), new Vector2(ax, ay));
            }
        }
    }

}