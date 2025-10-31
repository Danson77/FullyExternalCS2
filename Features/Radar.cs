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
        // 1) Process toggle key each frame
        HandleToggles();

        var config = ConfigManager.Load();
        var radarCfg = config.Esp.Radar;
        if (!radarCfg.Enabled) return;

        var player = graphics.GameData.Player;
        if (player == null) return;

        // snapshot
        RenderSnapshot snapshot;
        lock (player.RenderDataLock)
            snapshot = player.RenderData;
        if (snapshot == null) return;

        var entities = graphics.GameData.Entities;
        if (entities == null) return;

        float centerX = radarCfg.X + radarCfg.Size / 2f;
        float centerY = radarCfg.Y + radarCfg.Size / 2f;
        float radius = radarCfg.Size / 2f;

        // background + grid
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

        // local player dot + heading
        if (radarCfg.ShowLocalPlayer)
        {
            graphics.DrawCircleFilled(centerX, centerY, 5f, ToUintColor(SkiaSharp.SKColors.Cyan));
            graphics.DrawCircleOutline(centerX, centerY, 5f, ToUintColor(SkiaSharp.SKColors.White));
            graphics.DrawLine(ToUintColor(SkiaSharp.SKColors.White),
                new Vector2(centerX, centerY),
                new Vector2(centerX, centerY - 12f));
        }

        // heading
        var viewMatrix = snapshot.MatrixViewProjection;
        var forwardVector = new Vector2(viewMatrix.M13, viewMatrix.M23);
        float playerYawRad = MathF.Atan2(forwardVector.X, forwardVector.Y);
        float cosYaw = MathF.Cos(playerYawRad);
        float sinYaw = MathF.Sin(playerYawRad);

        foreach (var entity in entities)
        {
            if (!entity.IsAlive() || entity.AddressBase == player.AddressBase) continue;

            // 2) Hide teammates only when toggle is OFF (and TeamCheck is on).
            //    When TeamVisible == true, teammates are shown as well.
            if (config.TeamCheck && !TeamVisible && entity.Team == player.Team)
                continue;

            // world -> player-relative
            float relFwd = entity.Position.X - snapshot.Position.X;
            float relRight = entity.Position.Y - snapshot.Position.Y;

            float distUnits = MathF.Sqrt(relFwd * relFwd + relRight * relRight);
            float distMeters = distUnits * UnitsToMeters;
            if (distMeters > radarCfg.MaxDistance) continue;

            // rotate with player
            float rx = (relFwd * cosYaw) - (relRight * sinYaw);
            float ry = (relFwd * sinYaw) + (relRight * cosYaw);

            // to radar pixels
            float x = centerX + rx * UnitsToMeters * pixelsPerMeter;
            float y = centerY - ry * UnitsToMeters * pixelsPerMeter;

            // color by team & visibility
            string colorHex = (entity.Team == Team.Terrorists)
                ? radarCfg.EnemyColor
                : radarCfg.TeamColor;

            byte alpha = entity.IsVisible
                ? Convert.ToByte(radarCfg.VisibleAlpha, 16)
                : Convert.ToByte(radarCfg.InvisibleAlpha, 16);

            uint baseColor = Convert.ToUInt32(colorHex, 16);
            uint playerColor = ((uint)alpha << 24) | (baseColor & 0x00FFFFFFu);

            graphics.DrawCircleFilled(x, y, 3.5f, playerColor);
            graphics.DrawCircleOutline(x, y, 3.5f, ToUintColor(SkiaSharp.SKColors.White.WithAlpha(200)));

            // facing arrow
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