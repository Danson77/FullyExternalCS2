using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using CS2Cheat.Utils.CFGManager;
using SharpDX;
using Color = SharpDX.Color;

namespace CS2Cheat.Features;

public static class EspAimCrosshair
{
    private static Vector3 _pointClip = Vector3.Zero;

    private static Vector3 GetPositionScreen(GameProcess gameProcess, GameData gameData, Player player)
    {
        var screenSize = gameProcess.WindowRectangleClient.Size;
        var aspectRatio = (double)screenSize.Width / screenSize.Height;
        //var fovY = ((double)Player.Fov).DegreeToRadian();
        var fovY = GraphicsMath.DegreeToRadian((double)Player.Fov);
        var fovX = fovY * aspectRatio;
        var doPunch = player.ShotsFired > 0;
        //var punchX = doPunch ? ((double)player.AimPunchAngle.X * Offsets.WeaponRecoilScale).DegreeToRadian() : 0;
        //var punchY = doPunch ? ((double)player.AimPunchAngle.Y * Offsets.WeaponRecoilScale).DegreeToRadian() : 0;
        var punchX = doPunch ? GraphicsMath.DegreeToRadian((double)player.AimPunchAngle.X * Offsets.WeaponRecoilScale) : 0;
        var punchY = doPunch ? GraphicsMath.DegreeToRadian((double)player.AimPunchAngle.Y * Offsets.WeaponRecoilScale) : 0;
        _pointClip = new Vector3
        (
            (float)(-punchY / fovX),
            (float)(-punchX / fovY),
            0
        );
        return player.MatrixViewport.Transform(_pointClip);
    }

    public static void Draw(Graphics.Graphics graphics)
    {
        var player = graphics.GameData.Player;
        if (player == null)
            return;

        var cfg = ConfigManager.Load();
        var aimCfg = cfg?.Esp?.AimCrosshair ?? new ConfigManager.EspConfig.AimCrosshairConfig();
        if (!aimCfg.Enabled)
            return;

        var pointScreen = GetPositionScreen(graphics.GameProcess, graphics.GameData, player);
        Draw(graphics, new Vector2(pointScreen.X, pointScreen.Y), aimCfg);
    }

    private static void Draw(Graphics.Graphics graphics, Vector2 pointScreen, ConfigManager.EspConfig.AimCrosshairConfig aimCfg)
    {
        var crosshairRadius = aimCfg.Radius;
        DrawCrosshair(graphics, pointScreen, crosshairRadius);
    }

    private static void DrawCrosshair(Graphics.Graphics graphics, Vector2 pointScreen, int radius)
    {
        var color = Color.Red;

        graphics.DrawLine(color, pointScreen - new Vector2(radius, 0),
            pointScreen + new Vector2(radius, 0));
        graphics.DrawLine(color, pointScreen - new Vector2(0, radius),
            pointScreen + new Vector2(0, radius));
    }
}