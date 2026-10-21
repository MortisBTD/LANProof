using LANProof.App;
namespace LANProof.Tests;
public sealed class SingleInstanceTests
{
    [Fact] public void SecondLaunchSignalsPrimaryInsteadOfStartingAnotherSession()
    {
        if (!OperatingSystem.IsWindows()) return;
        var scope="LANProof-instance-test-"+Guid.NewGuid();
        using var restored=new ManualResetEventSlim();
        using(var primary=new SingleInstance(scope))
        {
            Assert.True(primary.IsPrimary); primary.Listen(()=>restored.Set());
            using var secondary=new SingleInstance(scope);
            Assert.False(secondary.IsPrimary); Assert.True(restored.Wait(TimeSpan.FromSeconds(3)));
        }
        using var next=new SingleInstance(scope); Assert.True(next.IsPrimary);
    }
}
