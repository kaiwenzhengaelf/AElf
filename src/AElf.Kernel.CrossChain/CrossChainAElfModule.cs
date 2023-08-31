using AElf.Kernel.Miner.Application;
using AElf.Kernel.SmartContract.Application;
using AElf.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace AElf.Kernel.CrossChain;

public class CrossChainAElfModule : AElfModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<ISystemTransactionGenerator, InlineSystemTransactionGenerator>();
        context.Services.AddSingleton<IInlineTransactionProvider, InlineTransactionProvider>();
    }
}