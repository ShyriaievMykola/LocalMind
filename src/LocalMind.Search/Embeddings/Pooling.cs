namespace LocalMind.Search.Embeddings;

/// <summary>Перетворює вектори окремих токенів на один нормалізований вектор тексту.</summary>
public static class Pooling
{
    /// <summary>Усереднює вектори токенів, ігноруючи доповнення (padding), і нормалізує результат.</summary>
    /// <param name="tokenEmbeddings">Вектори токенів одного тексту підряд, <c>токени × розмірність</c>.</param>
    /// <param name="attentionMask">1 для справжнього токена, 0 для доповнення.</param>
    /// <param name="dimensions">Розмірність вектора.</param>
    /// <returns>L2-нормалізований вектор довжини <paramref name="dimensions"/>.</returns>
    /// <exception cref="ArgumentException">Довжина <paramref name="tokenEmbeddings"/> не відповідає масці.</exception>
    public static float[] MeanPool(ReadOnlySpan<float> tokenEmbeddings, ReadOnlySpan<long> attentionMask, int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);

        if (tokenEmbeddings.Length != attentionMask.Length * dimensions)
        {
            throw new ArgumentException(
                $"Очікувалось {attentionMask.Length * dimensions} значень, отримано {tokenEmbeddings.Length}.",
                nameof(tokenEmbeddings));
        }

        var result = new float[dimensions];
        var count = 0;
        for (var token = 0; token < attentionMask.Length; token++)
        {
            if (attentionMask[token] == 0)
            {
                continue;
            }

            var embedding = tokenEmbeddings.Slice(token * dimensions, dimensions);
            for (var i = 0; i < dimensions; i++)
            {
                result[i] += embedding[i];
            }

            count++;
        }

        if (count > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                result[i] /= count;
            }
        }

        NormalizeL2(result);
        return result;
    }

    /// <summary>Нормалізує вектор до одиничної довжини; нульовий вектор лишає без змін.</summary>
    public static void NormalizeL2(Span<float> vector)
    {
        var sum = 0d;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        if (sum == 0)
        {
            return;
        }

        var norm = (float)Math.Sqrt(sum);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }
}
