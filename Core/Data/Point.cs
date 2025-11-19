using SharpDX;
using System.Runtime.InteropServices;

namespace CS2Cheat.Core.Data;

[StructLayout(LayoutKind.Sequential)]
public struct Point
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector4 Position;
    public ColorBGRA Color;
}

[StructLayout(LayoutKind.Sequential)]
public struct Rect
{
    public int Left, Top, Right, Bottom;
}
public enum Team
{
    Unknown = 0,
    Spectator = 1,
    Terrorists = 2,
    CounterTerrorists = 3
}