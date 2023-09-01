using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Standards.ACS0;
using AElf.Types;
using Google.Protobuf.Collections;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.Application;

public interface IInlineTransactionProvider
{
    Task<InlineTransactionInfo> GetInlineTransactionInfoAsync(IBlockIndex blockIndex);

    Task SetInlineTransactionInfoAsync(IBlockIndex blockIndex, InlineTransactionInfo inlineTransactionInfo);
}

internal class InlineTransactionProvider : BlockExecutedDataBaseProvider<TestInlineTransactionInfo>, IInlineTransactionProvider, ISingletonDependency
{
    private const string BlockExecutedDataName = nameof(InlineTransactionInfo);

    public InlineTransactionProvider(
        ICachedBlockchainExecutedDataService<TestInlineTransactionInfo> cachedBlockchainExecutedDataService) : base(
        cachedBlockchainExecutedDataService)
    {
    }

    public Task<InlineTransactionInfo> GetInlineTransactionInfoAsync(IBlockIndex blockIndex)
    {
        var inlineTransactionInfo = GetBlockExecutedData(blockIndex);
        var info = inlineTransactionInfo;
        return Task.FromResult(new InlineTransactionInfo
        {
            MerkleTreeRoot = info?.MerkleTreeRoot,
            TransactionIds = info?.TransactionIds.ToDictionary(pair => Hash.LoadFromHex(pair.Key), pair => pair.Value)
        });
    }

    public async Task SetInlineTransactionInfoAsync(IBlockIndex blockIndex, InlineTransactionInfo inlineTransactionInfo)
    {
        var map = new MapField<string, Transaction>();
        foreach (var pair in inlineTransactionInfo.TransactionIds)
        {
            map.Add(pair.Key.ToHex(), pair.Value);
        }
        await AddBlockExecutedDataAsync(blockIndex, new TestInlineTransactionInfo
        {
            MerkleTreeRoot = inlineTransactionInfo.MerkleTreeRoot,
            TransactionIds = { map }
        });
    }

    protected override string GetBlockExecutedDataName()
    {
        return BlockExecutedDataName;
    }
}

public class InlineTransactionInfo
{
    public Dictionary<Hash, Transaction> TransactionIds { get; set; }
    public Hash MerkleTreeRoot { get; set; }
}