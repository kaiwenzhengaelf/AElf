using System.Threading.Tasks;
using AElf.Types;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.Application;

public interface ISmartContractNameProvider
{
    Task<SmartContractName> GetSmartContractNameAsync(IChainContext chainContext, string address);

    Task SetSmartContractNameAsync(IBlockIndex blockIndex, string address, Hash contractName);
}

public class SmartContractNameProvider : BlockExecutedDataBaseProvider<SmartContractName>,
    ISmartContractNameProvider, ISingletonDependency
{
    public SmartContractNameProvider(ICachedBlockchainExecutedDataService<SmartContractName> cachedBlockchainExecutedDataService) : base(cachedBlockchainExecutedDataService)
    {
    }

    protected override string GetBlockExecutedDataName()
    {
        return nameof(SmartContractName);
    }

    public Task<SmartContractName> GetSmartContractNameAsync(IChainContext chainContext, string address)
    {
        var smartContractName = GetBlockExecutedData(chainContext, address);
        return Task.FromResult(smartContractName);
    }

    public async Task SetSmartContractNameAsync(IBlockIndex blockIndex, string address, Hash contractName)
    {
        await AddBlockExecutedDataAsync(blockIndex, address, new SmartContractName
        {
            Name = contractName,
            BlockHash = blockIndex.BlockHash,
            BlockHeight = blockIndex.BlockHeight
        });
    }
}