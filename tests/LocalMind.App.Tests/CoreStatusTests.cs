using LocalMind.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public class CoreStatusTests
{
    private readonly CoreStatus _status = new(NullLogger<CoreStatus>.Instance);

    [Fact]
    public async Task Finish_HandlerThrows_CompletionStillCompletesAndOtherHandlersRun()
    {
        var secondCalled = false;
        _status.Changed += (_, _) => throw new InvalidOperationException("збій обробника");
        _status.Changed += (_, _) => secondCalled = true;

        _status.SetReady();

        await _status.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CoreStage.Ready, _status.Stage);
        Assert.True(secondCalled);
    }

    [Fact]
    public void Completion_IsCompleteWhenChangedIsRaised()
    {
        var completedInHandler = false;
        _status.Changed += (_, _) => completedInHandler = _status.Completion.IsCompleted;

        _status.SetFailed("помилка");

        Assert.True(completedInHandler);
    }

    [Fact]
    public void SecondFinish_IsIgnored()
    {
        _status.SetFailed("помилка");
        _status.SetReady();

        Assert.Equal(CoreStage.Failed, _status.Stage);
        Assert.Equal("помилка", _status.Error);
    }

    [Fact]
    public void SetModelMissing_ThenAvailable_KeepsInvariant()
    {
        _status.SetModelMissing("немає model.onnx");
        Assert.False(_status.IsModelAvailable);
        Assert.Equal("немає model.onnx", _status.ModelError);

        _status.SetModelAvailable();
        Assert.True(_status.IsModelAvailable);
        Assert.Null(_status.ModelError);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyErrors_Throw(string error)
    {
        Assert.Throws<ArgumentException>(() => _status.SetModelMissing(error));
        Assert.Throws<ArgumentException>(() => _status.SetFailed(error));
    }
}
