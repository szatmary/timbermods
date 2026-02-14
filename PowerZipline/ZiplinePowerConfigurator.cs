using Bindito.Core;
using Timberborn.SingletonSystem;
namespace PowerZipline;

/// <summary>
/// Registers a singleton that retries graph merges after all entities are loaded
/// and on each game tick until pending merges resolve. Handles timing issues
/// where bridges are tracked before MechanicalNode graphs are initialized.
/// </summary>
[Context("Game")]
public class ZiplinePowerConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ZiplinePowerMergeRetrier>().AsSingleton();
    }
}

public class ZiplinePowerMergeRetrier : IPostLoadableSingleton, ILateUpdatableSingleton
{
    public void PostLoad()
    {
        ZiplinePowerTransferPatch.RetryPendingMerges();
    }

    public void LateUpdateSingleton()
    {
        ZiplinePowerTransferPatch.RetryPendingMerges();
    }
}
