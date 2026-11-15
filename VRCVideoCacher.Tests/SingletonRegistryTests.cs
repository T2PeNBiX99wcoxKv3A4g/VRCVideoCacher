using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Tests;

public class SingletonRegistryTests
{
    [Fact]
    public void KeepsTheRegisteredInstance()
    {
        var instance = new RegisteredFake();

        SingletonRegistry.Register(instance);

        Assert.Same(instance, SingletonRegistry.Find<RegisteredFake>());
        Assert.Same(instance, SingletonRegistry.Get<RegisteredFake>());
        Assert.Contains(instance, SingletonRegistry.All);
    }

    [Fact]
    public void RejectsASecondInstanceOfTheSameType()
    {
        SingletonRegistry.Register(new DuplicateFake());

        var exception = Assert.Throws<InvalidOperationException>(() => SingletonRegistry.Register(new DuplicateFake()));
        Assert.Contains(nameof(DuplicateFake), exception.Message);
    }

    [Fact]
    public void FindReturnsNullAndGetThrowsForAClassThatWasNeverCreated()
    {
        Assert.Null(SingletonRegistry.Find<MissingFake>());
        Assert.Throws<InvalidOperationException>(() => SingletonRegistry.Get<MissingFake>());
    }

    private sealed class RegisteredFake : ISingleton;

    private sealed class DuplicateFake : ISingleton;

    private sealed class MissingFake;
}
