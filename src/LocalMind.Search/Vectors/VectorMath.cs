using System.Numerics.Tensors;

namespace LocalMind.Search.Vectors;

/// <summary>Операції над векторами з апаратним прискоренням (SIMD).</summary>
public static class VectorMath
{
    /// <summary>
    /// Скалярний добуток. Для L2-нормалізованих векторів дорівнює косинусній схожості.
    /// </summary>
    /// <exception cref="ArgumentException">Вектори різної довжини.</exception>
    public static float DotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right) =>
        TensorPrimitives.Dot(left, right);
}
