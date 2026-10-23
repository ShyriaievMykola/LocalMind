using LocalMind.Core.Abstractions;

namespace LocalMind.TestUtilities;

/// <summary>Рахує токени як слова — замість токенізатора моделі.</summary>
public sealed class FakeTokenCounter : ITokenCounter
{
    public int CountTokens(string text) => TextTokens.Split(text).Count();
}
