using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.Application;

public interface IInlineTransactionProvider
{
    Task<InlineTransactionInfo> GetInlineTransactionInfoAsync(IChainContext chainContext);

    Task SetInlineTransactionInfoAsync(IBlockIndex blockIndex, InlineTransactionInfo inlineTransactionInfo);
}

public class InlineTransactionProvider : BlockExecutedDataBaseProvider<InlineTransactionInfo>, IInlineTransactionProvider, ISingletonDependency
{
    private const string BlockExecutedDataName = nameof(InlineTransactionInfo);

    public InlineTransactionProvider(
        ICachedBlockchainExecutedDataService<InlineTransactionInfo> cachedBlockchainExecutedDataService) : base(
        cachedBlockchainExecutedDataService)
    {
    }

    public Task<InlineTransactionInfo> GetInlineTransactionInfoAsync(IChainContext chainContext)
    {
        var inlineTransactionInfo = GetBlockExecutedData(chainContext);
        return Task.FromResult(inlineTransactionInfo);
    }

    public async Task SetInlineTransactionInfoAsync(IBlockIndex blockIndex, InlineTransactionInfo inlineTransactionInfo)
    {
        await AddBlockExecutedDataAsync(blockIndex, inlineTransactionInfo);
    }

    protected override string GetBlockExecutedDataName()
    {
        return BlockExecutedDataName;
    }
}

public class InlineTransactionInfo
{
    public List<string> List { get; set; }
}