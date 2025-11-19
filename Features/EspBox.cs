using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using SharpDX;
using SharpDX.Direct3D9;
using Color = SharpDX.Color;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

public static class EspBox
{
    // Updated HandleToggles to fix the toggle logic
    private static void HandleToggles()
    {
        // Check if the Team Visible toggle key is pressed
        if (toggleTeamVisibleKey.IsKeyDown() && !isTeamVisibleToggled)
        {
            TeamVisible = !TeamVisible;
            Console.WriteLine($"[+]Team (ESP) Visible: {TeamVisible}");
            isTeamVisibleToggled = true;  // Set the toggle state to prevent multiple triggers on the same key press
        }
        else if (!toggleTeamVisibleKey.IsKeyDown() && isTeamVisibleToggled)
        {
            // Reset the toggle state when the key is released
            isTeamVisibleToggled = false;
        }
    }

    private const int OutlineThickness = 2;

    private const float BoxScale = 1.20f;     // 30% bigger

    private static bool TeamVisible = false; // Toggle for targeting friends
    private static Keys toggleTeamVisibleKey = Keys.OemSemicolon; // Toggle key for friends ESP ('@')
    private static bool isTeamVisibleToggled = false; // To track key state

    private const float UnitsToMeters = 0.0254f;

    // Consider points "behind" if they’re within this forward tolerance
    private const float ForwardEpsilon = 0.05f; // tweak to 0.1 if you still see flicker

    private static bool WorldToScreenSafe(Graphics.Graphics g, Vector3 world, out Vector2 screen)
    {
        screen = default;

        var me = g.GameData.Player;
        var eyePos = me.EyePosition;

        var eyeDir = me.EyeDirection;
        if (eyeDir.LengthSquared() < 1e-6f) return false;
        var forward = Vector3.Normalize(eyeDir);

        // 1) Cull behind the camera (negative or too-close forward scalar)
        float t = Vector3.Dot(world - eyePos, forward);
        if (t <= ForwardEpsilon) return false;

        // 2) Project to screen using your combined View*Proj*Viewport matrix
        var sp = me.MatrixViewProjectionViewport.Transform(world);

        // 3) Valid depth range and finite XY
        if (!(sp.Z > 0f && sp.Z < 1f)) return false;
        if (!float.IsFinite(sp.X) || !float.IsFinite(sp.Y)) return false;

        screen = new Vector2(sp.X, sp.Y);
        return true;
    }

    private static void DrawHealthBar(Graphics.Graphics graphics, Vector2 topLeft, Vector2 bottomRight,
        float healthPercentage)
    {
        var barHeight = bottomRight.Y - topLeft.Y;
        var filledHeight = barHeight * healthPercentage;
        var filledTopLeft = new Vector2(topLeft.X, bottomRight.Y - filledHeight);
        var filledBottomRight = bottomRight;
    
        filledTopLeft.Y = Math.Max(filledTopLeft.Y, topLeft.Y);
        filledBottomRight.Y = Math.Min(filledBottomRight.Y, bottomRight.Y);
    
        graphics.DrawRectangle(Color.Green, filledTopLeft, filledBottomRight);
    
        var outlineTopLeft =
            new Vector2(topLeft.X - OutlineThickness, filledTopLeft.Y - OutlineThickness);
        var outlineBottomRight = new Vector2(bottomRight.X + OutlineThickness, bottomRight.Y + OutlineThickness);
        graphics.DrawRectangle(Color.Black, outlineTopLeft, outlineBottomRight);
    }
    private static void DrawEntityName(Graphics.Graphics graphics, (Vector2, Vector2) boundingBox, string entityName, bool isFriendly)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            entityName = "UNKNOWN";

        // Different colors for enemies and friends
        Color entityNameColor = isFriendly ? Color.Blue : Color.DeepPink; // LightBlue for teammates, DeepPink for enemies

        // Measure text size
        var textWidth = graphics.FontConsolas32.MeasureText(null, entityName, FontDrawFlags.Center).Right + 10f;

        // Positioning
        float offsetX = boundingBox.Item1.X + (boundingBox.Item2.X - boundingBox.Item1.X) / 2 - textWidth / 2;
        float offsetY = boundingBox.Item1.Y - 30f; // Move above entity box

        int outlineOffset = 1; // Thickness of outline
        Color outlineColor = Color.Black;

        // Draw outline first (render behind)
        graphics.FontConsolas32.DrawText(default, entityName, (int)offsetX - outlineOffset, (int)offsetY, outlineColor);
        graphics.FontConsolas32.DrawText(default, entityName, (int)offsetX + outlineOffset, (int)offsetY, outlineColor);
        graphics.FontConsolas32.DrawText(default, entityName, (int)offsetX, (int)offsetY - outlineOffset, outlineColor);
        graphics.FontConsolas32.DrawText(default, entityName, (int)offsetX, (int)offsetY + outlineOffset, outlineColor);

        // Draw main text on top
        graphics.FontConsolas32.DrawText(default, entityName, (int)offsetX, (int)offsetY, entityNameColor);
    }
    private static void DrawHealthNumber(Graphics.Graphics graphics, Vector2 topLeft, Vector2 bottomRight, int health)
    {
        // Get color based on health value (e.g., red for low, green for high)
        Color healthColor = GetHealthColor(health);

        // Convert health value to string for display
        var healthText = health.ToString();

        // Calculate X position
        var positionX = (int)(topLeft.X - 40);

        // Measure text size for vertical alignment
        var textSize = graphics.FontConsolas32.MeasureText(null, healthText, FontDrawFlags.Center);
        var textHeight = textSize.Bottom;

        // Calculate Y position: align text slightly above the top-left corner
        var positionY = (int)(topLeft.Y - textHeight - 5);

        // Define outline color
        Color outlineColor = Color.Black;

        // Draw black outline by offsetting text slightly in multiple directions
        graphics.FontConsolas36.DrawText(default, healthText, positionX - 1, positionY, outlineColor); // Left
        graphics.FontConsolas36.DrawText(default, healthText, positionX + 1, positionY, outlineColor); // Right
        graphics.FontConsolas36.DrawText(default, healthText, positionX, positionY - 1, outlineColor); // Top
        graphics.FontConsolas36.DrawText(default, healthText, positionX, positionY + 1, outlineColor); // Bottom

        // Draw main health text on top
        graphics.FontConsolas36.DrawText(default, healthText, positionX, positionY, healthColor);
    }

    private static Color GetHealthColor(int health)
    {
        // Normalize health percentage between 0 and 1
        float percentage = (float)health / 100f;
        percentage = MathUtil.Clamp(percentage, 0, 1); // Ensures percentage stays in [0, 1]

        // Interpolate between red (low health) and green (high health)
        int red = (int)(255 * (1 - percentage));
        int green = (int)(255 * percentage);
        return new Color(red, green, 0); // Blue is 0
    }

    private static bool TryGetEntityBoundingBox(Graphics.Graphics g, Entity e, out Vector2 min, out Vector2 max)
    {
        const float padding = 5.0f;
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        int valid = 0;

        foreach (var bone in e.BonePos)
        {
            if (!WorldToScreenSafe(g, bone.Value, out var s)) continue;
            valid++;
            if (s.X < min.X) min.X = s.X;
            if (s.Y < min.Y) min.Y = s.Y;
            if (s.X > max.X) max.X = s.X;
            if (s.Y > max.Y) max.Y = s.Y;
        }

        if (valid == 0) return false; // fully behind / off-screen → don't draw

        // padding
        min -= new Vector2(padding, padding);
        max += new Vector2(padding, padding);

        // scale about center
        var center = (min + max) * 0.5f;
        var halfSize = (max - min) * 0.5f * BoxScale;
        min = center - halfSize;
        max = center + halfSize;

        return true;
    }
    private static void DrawEntityRectangle(Graphics.Graphics graphics, Entity entity, Color color, (Vector2, Vector2) boundingBox)
    {
        var healthPercentage = (float)entity.Health / 100;

        graphics.DrawRectangle(color, boundingBox.Item1, boundingBox.Item2);

        var healthBarTopLeft = new Vector2(boundingBox.Item1.X - 10.0f - OutlineThickness, boundingBox.Item1.Y);
        var healthBarBottomRight = new Vector2(healthBarTopLeft.X + 6.0f, boundingBox.Item2.Y);
        DrawHealthBar(graphics, healthBarTopLeft, healthBarBottomRight, healthPercentage);

        DrawHealthNumber(graphics, boundingBox.Item1, boundingBox.Item2, entity.Health);

        //DrawWeaponName(graphics, boundingBox, entity.CurrentWeaponName);

        // Call DrawEntityName for ALL entities (friends & enemies)
        DrawEntityName(graphics, boundingBox, entity.Name, entity.Team == graphics.GameData.Player.Team);
    }
    public static void Draw(Graphics.Graphics graphics)
    {
        HandleToggles(); // do this once per frame
        var boxes = new Dictionary<Entity, (Vector2, Vector2)>();

        foreach (var ent in graphics.GameData.Entities)
        {
            if (!ent.IsAlive()) continue;
            if (ent.AddressBase == graphics.GameData.Player.AddressBase) continue;
            if (ent.Team == graphics.GameData.Player.Team && !TeamVisible) continue;

            if (TryGetEntityBoundingBox(graphics, ent, out var min, out var max))
                boxes.Add(ent, (min, max)); // only entities actually in front/on-screen
        }

        foreach (var ent in boxes.Keys)
        {
            var color = ent.Team == Team.Terrorists ? Color.DarkRed : Color.DarkBlue;
            DrawEntityRectangle(graphics, ent, color, boxes[ent]);
        }
    }
}