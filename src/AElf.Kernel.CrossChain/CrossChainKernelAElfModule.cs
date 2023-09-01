using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Blockchain.Domain;
using AElf.Kernel.Miner.Application;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using AElf.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace AElf.Kernel.CrossChain;

[DependsOn(typeof(SmartContractAElfModule))]
public class CrossChainKernelAElfModule : AElfModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<ISystemTransactionGenerator, InlineSystemTransactionGenerator>();
        context.Services.AddSingleton<IBlockchainService, FullBlockchainService>();
        context.Services.AddSingleton<ITransactionResultManager, TransactionResultManager>();
        context.Services.AddSingleton<ISmartContractAddressService, SmartContractAddressService>();
    }
}