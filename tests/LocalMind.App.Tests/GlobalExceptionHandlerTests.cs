using LocalMind.App.Services;

namespace LocalMind.App.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public void IsOnlyCancellation_OnlyCancellations_IncludingNested_IsTrue()
    {
        var exception = new AggregateException(
            new TaskCanceledException(),
            new OperationCanceledException(),
            new AggregateException(new TaskCanceledException()));

        Assert.True(GlobalExceptionHandler.IsOnlyCancellation(exception));
    }

    [Fact]
    public void IsOnlyCancellation_WithRealFailure_IsFalse()
    {
        var exception = new AggregateException(new TaskCanceledException(), new InvalidOperationException("збій"));

        Assert.False(GlobalExceptionHandler.IsOnlyCancellation(exception));
    }

    [Fact]
    public void ShouldHandle_OutOfMemory_IsFalse_OtherExceptionsAreTrue()
    {
        Assert.False(GlobalExceptionHandler.ShouldHandle(new OutOfMemoryException()));
        Assert.True(GlobalExceptionHandler.ShouldHandle(new InvalidOperationException()));
    }
}
