using BattleGame.Cli.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class DeviceIdentityStoreTests
{
    [Test]
    public void LoadOrCreate_ShouldPersistStableHighEntropyToken()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"BattleGame-device-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "device-token");
        try
        {
            var store = new DeviceIdentityStore(path);

            string first = store.LoadOrCreate();
            string second = store.LoadOrCreate();

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Length.EqualTo(64));
                Assert.That(first, Does.Match("^[0-9A-F]{64}$"));
                Assert.That(second, Is.EqualTo(first));
                Assert.That(File.ReadAllText(path).Trim(), Is.EqualTo(first));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
