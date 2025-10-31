using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Utils;
using SharpDX;
using Color = SharpDX.Color;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

public static class SkeletonEsp
{
    private static bool TeamVisible = false; // Toggle for targeting friends
    private static Keys toggleTeamVisibleKey = Keys.OemSemicolon; // Toggle key for friends ESP ('@')
    private static bool isTeamVisibleToggled = false; // To track key state

    // Updated HandleToggles to fix the toggle logic
    private static void HandleToggles()
    {
        // Check if the Team Visible toggle key is pressed
        if (toggleTeamVisibleKey.IsKeyDown() && !isTeamVisibleToggled)
        {
            TeamVisible = !TeamVisible;
            //Console.WriteLine($"[+]Team Visible (ESP) enabled: {TeamVisible}");
            isTeamVisibleToggled = true;  // Set the toggle state to prevent multiple triggers on the same key press
        }
        else if (!toggleTeamVisibleKey.IsKeyDown() && isTeamVisibleToggled)
        {
            // Reset the toggle state when the key is released
            isTeamVisibleToggled = false;
        }
    }

    public static void Draw(Graphics.Graphics graphics)
    {
        foreach (var entity in graphics.GameData.Entities)
        {

            if (!entity.IsAlive() || entity.AddressBase == graphics.GameData.Player.AddressBase) continue;

            HandleToggles();
            // Skip teammates if TeamVisible is false
            if (entity.Team == graphics.GameData.Player.Team && !TeamVisible)
                continue;

            // Set the color based on the entity's team
            var colorBones = entity.Team == Team.Terrorists ? Color.DarkOrange : Color.Green;

            // Draw bones for the current entity
            DrawBones(graphics, entity, colorBones);
        }
    }

    private static void DrawBones(Graphics.Graphics graphics, Entity entity, Color color)
    {
        // Define the bones connection data: (startBone, endBone)
        (string, string)[] bones =
        {
        ("head", "neck_0"),
        ("neck_0", "spine_1"),
        ("spine_1", "spine_2"),
        ("spine_2", "pelvis"),
        ("spine_1", "arm_upper_L"),
        ("arm_upper_L", "arm_lower_L"),
        ("arm_lower_L", "hand_L"),
        ("spine_1", "arm_upper_R"),
        ("arm_upper_R", "arm_lower_R"),
        ("arm_lower_R", "hand_R"),
        ("pelvis", "leg_upper_L"),
        ("leg_upper_L", "leg_lower_L"),
        ("leg_lower_L", "ankle_L"),
        ("pelvis", "leg_upper_R"),
        ("leg_upper_R", "leg_lower_R"),
        ("leg_lower_R", "ankle_R")
    };

        // Loop through each bone pair and draw a line between the connected bones
        foreach (var (startBone, endBone) in bones)
        {
            int lineThickness = 3; // Default thickness for offset lines

            // Adjust thickness for the middle line and offset lines
            int middleLineThickness = 5;  // Thicker line for the middle
            int offsetLineThickness = 1;  // Thinner lines for the offsets

            // Check if bone positions exist for the current entity
            if (entity.BonePos.ContainsKey(startBone) && entity.BonePos.ContainsKey(endBone))
            {
                var startBonePos = entity.BonePos[startBone];
                var endBonePos = entity.BonePos[endBone];

                // Draw the main (middle) line with thicker thickness
                DrawThickLine(graphics, color, startBonePos, endBonePos, middleLineThickness);

                // Draw the offset lines with thinner thickness and a different color
                Color offsetLineColor = Color.Black;
                DrawThickLine(graphics, offsetLineColor, startBonePos, endBonePos, offsetLineThickness);
            }
        }
    }

    // Helper function to draw the lines with varying thickness
    private static void DrawThickLine(Graphics.Graphics graphics, Color color, Vector3 startBonePos, Vector3 endBonePos, int lineThickness)
    {
        // Loop to draw lines with the specified thickness
        for (int i = -lineThickness / 3; i <= lineThickness / 3; i++)
        {
            // Create an offset for drawing thicker lines
            var offsetStart = new Vector3(startBonePos.X + i, startBonePos.Y + i, startBonePos.Z);
            var offsetEnd = new Vector3(endBonePos.X + i, endBonePos.Y + i, endBonePos.Z);

            // Draw the line with the offset
            graphics.DrawLineWorld(color, offsetStart, offsetEnd);
        }
    }
}