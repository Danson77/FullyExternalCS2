using SharpDX;
//using System.Numerics;
using System.Drawing;

namespace CS2Cheat.Graphics;
public struct ViewportInfo
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;
}

public static class GraphicsMath
{
    //public static Vector3 GetVectorFromEulerAngles(double phi, double theta)
    //{
    //    return new Vector3
    //    (
    //        (float)(Math.Cos(phi) * Math.Cos(theta)),
    //        (float)(Math.Cos(phi) * Math.Sin(theta)),
    //        (float)-Math.Sin(phi)
    //    ).GetNormalized();
    //}

    public static Vector3 GetVectorFromEulerAngles(double phi, double theta)
    {
        return Vector3.Normalize(new Vector3
        (
            (float)(Math.Cos(phi) * Math.Cos(theta)),
            (float)(Math.Cos(phi) * Math.Sin(theta)),
            (float)-Math.Sin(phi)
        ));
    }

    public static Matrix GetMatrixViewport(Size screenSize)
    {
        return GetMatrixViewport(new Viewport
        {
            X = 0,
            Y = 0,
            Width = screenSize.Width,
            Height = screenSize.Height,
            MinDepth = 0,
            MaxDepth = 1
        });
    }

    //public static Matrix4x4 GetMatrixViewport(Size screenSize)
    //{
    //    var viewport = new ViewportInfo
    //    {
    //        X = 0,
    //        Y = 0,
    //        Width = screenSize.Width,
    //        Height = screenSize.Height,
    //        MinDepth = 0.0f,
    //        MaxDepth = 1.0f
    //    };
    //
    //    return GetMatrixViewport(viewport);
    //}

    private static Matrix GetMatrixViewport(in Viewport viewport)
    {
        return new Matrix
        {
            M11 = viewport.Width * 0.5f,
            M12 = 0,
            M13 = 0,
            M14 = 0,

            M21 = 0,
            M22 = -viewport.Height * 0.5f,
            M23 = 0,
            M24 = 0,

            M31 = 0,
            M32 = 0,
            M33 = viewport.MaxDepth - viewport.MinDepth,
            M34 = 0,

            M41 = viewport.X + viewport.Width * 0.5f,
            M42 = viewport.Y + viewport.Height * 0.5f,
            M43 = viewport.MinDepth,
            M44 = 1
        };
    }
    //private static Matrix4x4 GetMatrixViewport(ViewportInfo viewport)
    //{
    //    return new Matrix4x4(
    //        viewport.Width * 0.5f, 0, 0, 0,
    //        0, -viewport.Height * 0.5f, 0, 0,
    //        0, 0, viewport.MaxDepth - viewport.MinDepth, 0,
    //        viewport.X + viewport.Width * 0.5f,
    //        viewport.Y + viewport.Height * 0.5f,
    //        viewport.MinDepth,
    //        1.0f
    //    );
    //}

    public static Vector3 Transform(this in Matrix matrix, Vector3 value)
    {
        var wInv = 1.0 / (matrix.M14 * (double)value.X + matrix.M24 * (double)value.Y +
                          matrix.M34 * (double)value.Z + matrix.M44);
        return new Vector3
        (
            (float)((matrix.M11 * (double)value.X + matrix.M21 * (double)value.Y +
                     matrix.M31 * (double)value.Z + matrix.M41) * wInv),
            (float)((matrix.M12 * (double)value.X + matrix.M22 * (double)value.Y +
                     matrix.M32 * (double)value.Z + matrix.M42) * wInv),
            (float)((matrix.M13 * (double)value.X + matrix.M23 * (double)value.Y +
                     matrix.M33 * (double)value.Z + matrix.M43) * wInv)
        );
    }
    //public static Vector3 Transform(this Matrix4x4 matrix, Vector3 value)
    //{
    //    var wInv = 1.0 / (matrix.M14 * value.X + matrix.M24 * value.Y +
    //                      matrix.M34 * value.Z + matrix.M44);
    //
    //    return new Vector3(
    //        (float)((matrix.M11 * value.X + matrix.M21 * value.Y +
    //                matrix.M31 * value.Z + matrix.M41) * wInv),
    //        (float)((matrix.M12 * value.X + matrix.M22 * value.Y +
    //                matrix.M32 * value.Z + matrix.M42) * wInv),
    //        (float)((matrix.M13 * value.X + matrix.M23 * value.Y +
    //                matrix.M33 * value.Z + matrix.M43) * wInv)
    //    );
    //}

    public static System.Numerics.Vector3 Transform2(this in Matrix matrix, System.Numerics.Vector3 value)
    {
        var wInv = 1.0 / (matrix.M14 * (double)value.X + matrix.M24 * (double)value.Y +
                          matrix.M34 * (double)value.Z + matrix.M44);
        return new System.Numerics.Vector3
        (
            (float)((matrix.M11 * (double)value.X + matrix.M21 * (double)value.Y +
                     matrix.M31 * (double)value.Z + matrix.M41) * wInv),
            (float)((matrix.M12 * (double)value.X + matrix.M22 * (double)value.Y +
                     matrix.M32 * (double)value.Z + matrix.M42) * wInv),
            (float)((matrix.M13 * (double)value.X + matrix.M23 * (double)value.Y +
                     matrix.M33 * (double)value.Z + matrix.M43) * wInv)
        );
    }

    //public static float GetAngleTo(this Vector3 vector, Vector3 other)
    //{
    //    return GetAngleBetweenUnitVectors(vector.GetNormalized(), other.GetNormalized());
    //}

    public static float GetAngleTo(this Vector3 from, Vector3 to)
    {
        var dot = Vector3.Dot(Vector3.Normalize(from), Vector3.Normalize(to));
        dot = Math.Clamp(dot, -1.0f, 1.0f);
        return MathF.Acos(dot);
    }

    private static float GetAngleBetweenUnitVectors(Vector3 leftNormalized, Vector3 rightNormalized)
    {
        return AcosClamped(Vector3.Dot(leftNormalized, rightNormalized));
    }


    //public static Vector3 GetNormalized(this Vector3 value)
    //{
    //    return Vector3.Normalize(value);
    //}

    public static Vector3 GetNormalized(this Vector3 vector)
    {
        return Vector3.Normalize(vector);
    }


    private static bool IsParallelTo(this Vector3 vector, Vector3 other, float tolerance = 1E-6f)
    {
        return Math.Abs(1.0 - Math.Abs(Vector3.Dot(vector.GetNormalized(), other.GetNormalized()))) <= tolerance;
    }

    //public static float GetSignedAngleTo(this Vector3 vector, Vector3 other, Vector3 about)
    //{
    //    if (vector.IsParallelTo(about, 1E-9f))
    //        throw new ArgumentException($"'{nameof(vector)}' is parallel to '{nameof(about)}'.");
    //    if (other.IsParallelTo(about, 1E-9f))
    //        throw new ArgumentException($"'{nameof(other)}' is parallel to '{nameof(about)}'.");
    //
    //    var plane = new Plane3D(about, new Vector3());
    //    var vectorOnPlane = plane.ProjectVector(vector).vector.GetNormalized();
    //    var otherOnPlane = plane.ProjectVector(other).vector.GetNormalized();
    //    var crossProduct = Vector3.Cross(vectorOnPlane, otherOnPlane).GetNormalized();
    //    var sign = Vector3.Dot(crossProduct, plane.Normal);
    //    return GetAngleBetweenUnitVectors(vectorOnPlane, otherOnPlane) * sign;
    //}

    public static float GetSignedAngleTo(this Vector3 from, Vector3 to, Vector3 axis)
    {
        var angle = GetAngleTo(from, to);
        var cross = Vector3.Cross(from, to);
        var sign = Vector3.Dot(cross, axis) < 0 ? -1.0f : 1.0f;
        return angle * sign;
    }


    private static float AcosClamped(float value, float tolerance = 1E-6f)
    {
        if (value > 1 - tolerance) return 0;
        if (value < tolerance - 1) return (float)Math.PI;
        return (float)Math.Acos(value);
    }

    public static double DegreeToRadian(this double angle)
    {
        return Math.PI * angle / 180.0;
    }

    public static double RadianToDegree(this double angle)
    {
        return angle * (180.0 / Math.PI);
    }

    public static float DegreeToRadian(this float angle)
    {
        return (float)(Math.PI * angle / 180.0);
    }

    public static float RadianToDegree(this float angle)
    {
        return (float)(angle * (180.0 / Math.PI));
    }
}