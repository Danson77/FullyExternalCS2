using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Graphics;
using CS2Cheat.Utils.CFGManager;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Forms;
using static CS2Cheat.Core.User32;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Features;

internal static class EspColor
{
    public const uint White = 0xFFFFFFFF;
    public const uint Green = 0xFF00FF00;
    public const uint Black = 0xFF000000;
    public const uint Red = 0xFFFF0000;
    public const uint Yellow = 0xFFFFFF00;
    public const uint Orange = 0xFFFFA500;
    public const uint Gray = 0xFF808080;
}

public static class BoxExtra
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

    private static readonly Dictionary<string, string> GunIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        // Knif
        ["knife"] = "[",
        ["knife_t"] = "[",
        ["knife_ct"] = "]",
        ["bayonet"] = "p",
        ["flipknife"] = "q",
        ["gutknife"] = "r",
        ["karambit"] = "s",
        ["m9bayonet"] = "t",
        ["tacticalknife"] = "u",
        ["butterflyknife"] = "v",
        ["falchionknife"] = "w",
        ["shadowdaggers"] = "x",
        ["paracordknife"] = "y",
        ["survivalknife"] = "z",
        ["ursusknife"] = "{",
        ["navajaknife"] = "|",
        ["nomadknife"] = "}",
        ["stilettoknife"] = "~",
        ["talonknife"] = "⌂",
        ["classicknife"] = "Ç",

        // Pistoles
        ["deagle"] = "A",
        ["elite"] = "B",
        ["fiveseven"] = "C",
        ["glock"] = "D",
        ["hkp2000"] = "E",
        ["p250"] = "F",
        ["usp_silencer"] = "G",
        ["tec9"] = "H",
        ["cz75a"] = "I",
        ["revolver"] = "J",

        // SMG
        ["mac10"] = "K",
        ["mp9"] = "L",
        ["mp7"] = "M",
        ["ump45"] = "N",
        ["p90"] = "O",
        ["bizon"] = "P",

        // Machi
        ["ak47"] = "Q",
        ["aug"] = "R",
        ["famas"] = "S",
        ["galilar"] = "T",
        ["m4a1"] = "U",
        ["m4a1_silencer"] = "V",
        ["sg556"] = "W",

        // Sniper
        ["awp"] = "X",
        ["g3sg1"] = "Y",
        ["scar20"] = "Z",
        ["ssg08"] = "a",

        // Shotguns
        ["mag7"] = "b",
        ["nova"] = "c",
        ["sawedoff"] = "d",
        ["xm1014"] = "e",

        // Negevs
        ["m249"] = "f",
        ["negev"] = "g",

        // Non leathal
        ["taser"] = "h",
        ["c4"] = "o",

        // Granades
        ["flashbang"] = "i",
        ["hegrenade"] = "j",
        ["smokegrenade"] = "k",
        ["molotov"] = "l",
        ["decoy"] = "m",
        ["incgrenade"] = "n"
    };

    // Status
    private static readonly Dictionary<string, string> StatusIcons = new()
    {
        ["Flashed"] = "💡",
        ["Scoped"] = "🔎",
        ["Defusing"] = "💣",
        ["Air"] = "🪂",
        ["Running"] = "🏃",
        ["Walking"] = "🚶"
    };

    private static (Vector2, Vector2)? GetEntityBoundingBox(Player player, Entity entity)
    {
        const float BasePadding = 5.0f;
        var minPos = new Vector2(float.MaxValue, float.MaxValue);
        var maxPos = new Vector2(float.MinValue, float.MinValue);

        var matrix = player.MatrixViewProjectionViewport;
        if (entity.BonePos == null || entity.BonePos.Count == 0)
            return null;

        bool anyValid = false;
        foreach (var bone in entity.BonePos.Values)
        {
            var projected = matrix.Transform(bone);
            if (projected.Z >= 1 || projected.X < 0 || projected.Y < 0)
                continue;

            anyValid = true;
            minPos.X = Math.Min(minPos.X, projected.X);
            minPos.Y = Math.Min(minPos.Y, projected.Y);
            maxPos.X = Math.Max(maxPos.X, projected.X);
            maxPos.Y = Math.Max(maxPos.Y, projected.Y);
        }

        if (!anyValid)
            return null;

        var sizeMultiplier = 2f - (entity.Health / 100f);
        var padding = new Vector2(BasePadding * sizeMultiplier);

        return (minPos - padding, maxPos + padding);
    }

    private static void DrawCenteredText(ModernGraphics graphics, string text, float centerX, float y, uint color, float fontSize = 16, bool useCustomFont = false)
    {
        var textSize = graphics.MeasureText(text, fontSize, useCustomFont);
        float textWidth = textSize.X;
        float textX = centerX - textWidth / 2f;
        graphics.DrawText(text, textX, y, color, fontSize, useCustomFont);
    }

    private static string GetWeaponIcon(string? weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return string.Empty;
        string cleanName = weaponName.Replace("weapon_", "", StringComparison.OrdinalIgnoreCase);
        return GunIcons.GetValueOrDefault(cleanName, "?");
    }

    private static uint SetAlpha(uint color, byte alpha)
    {
        return ((uint)alpha << 24) | (color & 0x00FFFFFFu);
    }

    private static void DrawEntityEsp(
        ModernGraphics graphics,
        Player localPlayer,
        Entity entity,
        (Vector2 TopLeft, Vector2 BottomRight) bbox,
        ConfigManager.EspConfig.BoxExtraConfig config)
    {
        var (topLeft, bottomRight) = bbox;
        if (topLeft.X >= bottomRight.X || topLeft.Y >= bottomRight.Y) return;

        // === Check Team ===
        bool isVisible = entity.IsVisible;
        string colorHex = entity.Team == Team.Terrorists
            ? config.EnemyColor
            : config.TeamColor;

        byte alpha = isVisible
            ? Convert.ToByte(config.VisibleAlpha, 16)
            : Convert.ToByte(config.InvisibleAlpha, 16);

        uint baseColor = Convert.ToUInt32(colorHex, 16);
        uint boxColor = SetAlpha(baseColor, alpha);

        // === Box Preofest ===
        float width = bottomRight.X - topLeft.X;
        float height = bottomRight.Y - topLeft.Y;
        //graphics.DrawRectOutline(topLeft.X, topLeft.Y, width, height, boxColor);


        float textXb = topLeft.X - 25;
        float textY = topLeft.Y - 50;
        float centerX = (topLeft.X + bottomRight.X) / 2f;

        // If Has bomb
        bool hasBomb = !string.IsNullOrEmpty(entity.CurrentWeaponName) && entity.CurrentWeaponName.Contains("c4", StringComparison.OrdinalIgnoreCase);
        if (hasBomb)
        {
            string bombText = "💣";
            DrawCenteredText(graphics, bombText, textXb, textY, EspColor.Black, 20);
        }

        // === Distance ===
        float distance = SharpDX.Vector3.Distance(localPlayer.Position, entity.Position) * UnitsToMeters;
        if (config.ShowDistance & distance > 5)
        {
            string distText = $"{distance:0}m";
            DrawCenteredText(graphics, distText, centerX, textY, EspColor.Red, 20);
        }

        // === Health Bar ===
        //if (config.ShowHealthBar)
        //{
        //    float healthBarLeft = topLeft.X - 8f;
        //    float healthBarHeight = height;
        //    float healthBarWidth = 4f;
        //
        //    float healthPercentage = Math.Clamp(entity.Health / 100f, 0f, 1f);
        //    float filledHeight = healthBarHeight * healthPercentage;
        //    float filledTopY = topLeft.Y + (healthBarHeight - filledHeight);
        //
        //    uint healthBarColor = entity.Health > 60 ? EspColor.Green :
        //                           entity.Health > 30 ? EspColor.Yellow : EspColor.Red;
        //
        //    graphics.DrawRect(healthBarLeft, topLeft.Y, healthBarWidth, healthBarHeight, 0x80000000);
        //    graphics.DrawRect(healthBarLeft, filledTopY, healthBarWidth, filledHeight, healthBarColor);
        //    graphics.DrawRectOutline(healthBarLeft - 1, topLeft.Y - 1, healthBarWidth + 2, healthBarHeight + 2, EspColor.Black);
        //}


        // === Armo Icon with Number ===
        if (config.ShowArmor && entity.Armor > 0)
        {
            string armorText = entity.HasHelmet ? $"🛡{entity.Armor}" : $"🥚{entity.Armor}";
            int armorX = (int)(bottomRight.X + 5);
            int armorY = (int)(bottomRight.Y + 10);
            graphics.DrawText(armorText, armorX, armorY, EspColor.Red, 15);
        }

        // === Weapon Icon ===
        if (config.ShowWeaponIcon && !string.IsNullOrEmpty(entity.CurrentWeaponName))
        {
            string icon = GetWeaponIcon(entity.CurrentWeaponName);
            if (!string.IsNullOrEmpty(icon))
            {
                int weaponY = (int)(bottomRight.Y + 25);
                bool useCustom = graphics is ModernGraphics mg && mg.IsUndefeatedFontLoaded;
                DrawCenteredText(graphics, icon, centerX, weaponY, EspColor.Orange, 20, useCustom);
            }
        }

        // === Emoji Status ===
        if (config.ShowFlags)
        {
            int flagX = (int)(bottomRight.X + 10);
            int flagY = (int)topLeft.Y;
            int spacing = 18;
            int line = 0;

            if (entity.FlashAlpha > 7)
            {
                graphics.DrawText($"{StatusIcons["Flashed"]} Flashed", flagX, flagY + line * spacing, EspColor.Green);
                line++;
            }

            if (entity.IsInScope == 1)
            {
                graphics.DrawText($"{StatusIcons["Scoped"]} Scoped", flagX, flagY + line * spacing, EspColor.Orange);
                line++;
            }

            if (entity.IsDefusing)
            {
                graphics.DrawText($"{StatusIcons["Defusing"]} Defusing", flagX, flagY + line * spacing, EspColor.Red);
                line++;
            }

            if (!entity.Flags.HasFlag(EntityFlags.OnGround))
            {
                graphics.DrawText($"{StatusIcons["Air"]} Air", flagX, flagY + line * spacing, EspColor.Red);
                line++;
            }

            float speed = entity.Velocity.Length();
            if (speed > 200f)
            {
                graphics.DrawText($"{StatusIcons["Running"]} Running", flagX, flagY + line * spacing, EspColor.Green);
                line++;
            }
            else if (speed > 10f)
            {
                graphics.DrawText($"{StatusIcons["Walking"]} Walking", flagX, flagY + line * spacing, EspColor.Orange);
                line++;
            }
        }
    }
    public static void Draw(ModernGraphics graphics)
    {
        HandleToggles(); // do this once per frame

        var fullConfig = ConfigManager.Load();
        var espConfig = fullConfig.Esp.BoxExtra;

        if (!espConfig.Enabled) return;

        var player = graphics.GameData.Player;
        var entities = graphics.GameData.Entities;
        if (player == null || entities == null) return;

        foreach (var entity in entities)
        {
            if (!entity.IsAlive() || entity.AddressBase == player.AddressBase) continue;

            bool isTeammate = entity.Team == player.Team;
            if (!TeamVisible && fullConfig.TeamCheck && isTeammate) continue;

            var bbox = GetEntityBoundingBox(player, entity);
            if (bbox == null) continue;

            DrawEntityEsp(graphics, player, entity, bbox.Value, espConfig);
        }
    }
}