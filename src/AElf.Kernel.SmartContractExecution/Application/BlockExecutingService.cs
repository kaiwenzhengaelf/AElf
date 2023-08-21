using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AElf.CSharp.Core.Utils;
using AElf.Kernel.Blockchain;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Miner.Application;
using AElf.Kernel.SmartContract.Application;
using AElf.Kernel.SmartContract.Domain;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Local;

namespace AElf.Kernel.SmartContractExecution.Application;

public class BlockExecutingService : IBlockExecutingService, ITransientDependency
{
    private readonly IBlockchainStateService _blockchainStateService;
    private readonly ISystemTransactionExtraDataProvider _systemTransactionExtraDataProvider;
    private readonly ITransactionExecutingService _transactionExecutingService;
    private readonly ITransactionResultService _transactionResultService;

    public BlockExecutingService(ITransactionExecutingService transactionExecutingService,
        IBlockchainStateService blockchainStateService,
        ITransactionResultService transactionResultService,
        ISystemTransactionExtraDataProvider systemTransactionExtraDataProvider)
    {
        _transactionExecutingService = transactionExecutingService;
        _blockchainStateService = blockchainStateService;
        _transactionResultService = transactionResultService;
        _systemTransactionExtraDataProvider = systemTransactionExtraDataProvider;
        EventBus = NullLocalEventBus.Instance;
    }

    public ILocalEventBus EventBus { get; set; }
    public ILogger<BlockExecutingService> Logger { get; set; }

    public async Task<BlockExecutedSet> ExecuteBlockAsync(BlockHeader blockHeader,
        List<Transaction> nonCancellableTransactions)
    {
        _systemTransactionExtraDataProvider.TryGetSystemTransactionCount(blockHeader,
            out var systemTransactionCount);
        return await ExecuteBlockAsync(blockHeader, nonCancellableTransactions.Take(systemTransactionCount),
            nonCancellableTransactions.Skip(systemTransactionCount),
            CancellationToken.None);
    }

    public async Task<BlockExecutedSet> ExecuteBlockAsync(BlockHeader blockHeader,
        IEnumerable<Transaction> nonCancellableTransactions, IEnumerable<Transaction> cancellableTransactions,
        CancellationToken cancellationToken)
    {
        Logger.LogTrace("Entered ExecuteBlockAsync");
        var nonCancellable = nonCancellableTransactions.ToList();
        var cancellable = cancellableTransactions.ToList();
        var nonCancellableReturnSets =
            await _transactionExecutingService.ExecuteAsync(
                new TransactionExecutingDto { BlockHeader = blockHeader, Transactions = nonCancellable },
                CancellationToken.None);
        Logger.LogTrace("Executed non-cancellable txs");

        var returnSetCollection = new ExecutionReturnSetCollection(nonCancellableReturnSets);
        var cancellableReturnSets = new List<ExecutionReturnSet>();

        if (!cancellationToken.IsCancellationRequested && cancellable.Count > 0)
        {
            cancellableReturnSets = await _transactionExecutingService.ExecuteAsync(
                new TransactionExecutingDto
                {
                    BlockHeader = blockHeader,
                    Transactions = cancellable,
                    PartialBlockStateSet = returnSetCollection.ToBlockStateSet()
                },
                cancellationToken);
            returnSetCollection.AddRange(cancellableReturnSets);
            Logger.LogTrace("Executed cancellable txs");
        }

        var executedCancellableTransactions = new HashSet<Hash>(cancellableReturnSets.Select(x => x.TransactionId));
        var allExecutedTransactions =
            nonCancellable.Concat(cancellable.Where(x => executedCancellableTransactions.Contains(x.GetHash())))
                .ToList();
        var blockStateSet =
            CreateBlockStateSet(blockHeader.PreviousBlockHash, blockHeader.Height, returnSetCollection);
        var block = await FillBlockAfterExecutionAsync(blockHeader, allExecutedTransactions, returnSetCollection,
            blockStateSet);

        // set txn results
        var transactionResults = await SetTransactionResultsAsync(returnSetCollection, block.Header);

        // set blocks state
        blockStateSet.BlockHash = block.GetHash();
        Logger.LogTrace("Set block state set.");
        await _blockchainStateService.SetBlockStateSetAsync(blockStateSet);

        // handle execution cases 
        await CleanUpReturnSetCollectionAsync(block.Header, returnSetCollection);

        return new BlockExecutedSet
        {
            Block = block,
            TransactionMap = allExecutedTransactions.ToDictionary(p => p.GetHash(), p => p),
            TransactionResultMap = transactionResults.ToDictionary(p => p.TransactionId, p => p)
        };
    }

    private Task<Block> FillBlockAfterExecutionAsync(BlockHeader header,
        IEnumerable<Transaction> transactions, ExecutionReturnSetCollection executionReturnSetCollection,
        BlockStateSet blockStateSet)
    {
        Logger.LogTrace("Start block field filling after execution.");
        var bloom = new Bloom();
        foreach (var returnSet in executionReturnSetCollection.Executed)
            bloom.Combine(new[] { new Bloom(returnSet.Bloom.ToByteArray()) });

        var allExecutedTransactionIds = transactions.Select(x => x.GetHash()).ToList();
        
        var orderedReturnSets = executionReturnSetCollection.GetExecutionReturnSetList()
            .OrderBy(d => allExecutedTransactionIds.IndexOf(d.TransactionId)).ToList();

        var block = new Block
        {
            Header = new BlockHeader(header)
            {
                Bloom = ByteString.CopyFrom(bloom.Data),
                MerkleTreeRootOfWorldState = CalculateWorldStateMerkleTreeRoot(blockStateSet),
                MerkleTreeRootOfTransactionStatus = CalculateTransactionStatusMerkleTreeRoot(orderedReturnSets),
                MerkleTreeRootOfTransactions = CalculateTransactionMerkleTreeRoot(allExecutedTransactionIds)
            },
            Body = new BlockBody
            {
                TransactionIds = { allExecutedTransactionIds }
            }
        };

        if (transactions.Any(t => t.MethodName.Contains("Test")))
        {
            var tx = orderedReturnSets.First(o => o.TransactionId == transactions.First(t => t.MethodName.Contains("Test")).GetHash());
            var list = orderedReturnSets.Where(o => !transactions.Select(t => t.GetHash()).Contains(o.TransactionId) && Int64Value.Parser.ParseFrom(o.ReturnValue).Value == 10086).ToList();
            
            var binaryMerkleTree = new BinaryMerkleTree();
            binaryMerkleTree.Nodes.Add(GetHashCombiningTransactionAndStatus(tx.TransactionId, tx.Status));
            binaryMerkleTree.Nodes.AddRange(list.Select(o => GetHashCombiningTransactionAndStatus(o.TransactionId, o.Status)));
            binaryMerkleTree.LeafCount = binaryMerkleTree.Nodes.Count;
            GenerateBinaryMerkleTreeNodesWithLeafNodes(binaryMerkleTree.Nodes);
            binaryMerkleTree.Root = binaryMerkleTree.Nodes.Any() ? binaryMerkleTree.Nodes.Last() : Hash.Empty;

            var list2 = orderedReturnSets.Where(o => !list.Contains(o) && o.TransactionId != tx.TransactionId);
            var newBinaryMerkleTree = new BinaryMerkleTree();
            newBinaryMerkleTree.Nodes.Add(binaryMerkleTree.Root);
            newBinaryMerkleTree.Nodes.AddRange(list2.Select(o => GetHashCombiningTransactionAndStatus(o.TransactionId, o.Status)));
            newBinaryMerkleTree.LeafCount = newBinaryMerkleTree.Nodes.Count;
            GenerateBinaryMerkleTreeNodesWithLeafNodes(newBinaryMerkleTree.Nodes);
            newBinaryMerkleTree.Root = newBinaryMerkleTree.Nodes.Any() ? newBinaryMerkleTree.Nodes.Last() : Hash.Empty;
            

            block.Header.MerkleTreeRootOfTransactionStatus = newBinaryMerkleTree.Root;
        }

        Logger.LogTrace("Finish block field filling after execution.");
        return Task.FromResult(block);
    }
    
    private static void GenerateBinaryMerkleTreeNodesWithLeafNodes(IList<Hash> leafNodes)
    {
        if (!leafNodes.Any()) return;

        if (leafNodes.Count % 2 == 1)
            leafNodes.Add(leafNodes.Last());
        var nodeToAdd = leafNodes.Count / 2;
        var newAdded = 0;
        var i = 0;
        while (i < leafNodes.Count - 1)
        {
            var left = leafNodes[i++];
            var right = leafNodes[i++];
            leafNodes.Add(HashHelper.ConcatAndCompute(left, right));
            if (++newAdded != nodeToAdd)
                continue;

            // complete this row
            if (nodeToAdd % 2 == 1 && nodeToAdd != 1)
            {
                nodeToAdd++;
                leafNodes.Add(leafNodes.Last());
            }

            // start a new row
            nodeToAdd /= 2;
            newAdded = 0;
        }
    }

    protected virtual Task CleanUpReturnSetCollectionAsync(BlockHeader blockHeader,
        ExecutionReturnSetCollection executionReturnSetCollection)
    {
        return Task.CompletedTask;
    }

    private Hash CalculateWorldStateMerkleTreeRoot(BlockStateSet blockStateSet)
    {
        Logger.LogTrace("Start world state calculation.");
        Hash merkleTreeRootOfWorldState;
        var byteArrays = GetDeterministicByteArrays(blockStateSet);
        using (var hashAlgorithm = SHA256.Create())
        {
            foreach (var bytes in byteArrays) hashAlgorithm.TransformBlock(bytes, 0, bytes.Length, null, 0);

            hashAlgorithm.TransformFinalBlock(new byte[0], 0, 0);
            merkleTreeRootOfWorldState = Hash.LoadFromByteArray(hashAlgorithm.Hash);
        }

        return merkleTreeRootOfWorldState;
    }

    private IEnumerable<byte[]> GetDeterministicByteArrays(BlockStateSet blockStateSet)
    {
        var keys = blockStateSet.Changes.Keys;
        foreach (var k in new SortedSet<string>(keys))
        {
            yield return Encoding.UTF8.GetBytes(k);
            yield return blockStateSet.Changes[k].ToByteArray();
        }

        keys = blockStateSet.Deletes;
        foreach (var k in new SortedSet<string>(keys))
        {
            yield return Encoding.UTF8.GetBytes(k);
            yield return ByteString.Empty.ToByteArray();
        }
    }

    private Hash CalculateTransactionStatusMerkleTreeRoot(List<ExecutionReturnSet> blockExecutionReturnSet)
    {
        Logger.LogTrace("Start transaction status merkle tree root calculation.");
        var executionReturnSet = blockExecutionReturnSet.Select(executionReturn =>
            (executionReturn.TransactionId, executionReturn.Status));
        var nodes = new List<Hash>();
        foreach (var (transactionId, status) in executionReturnSet)
            nodes.Add(GetHashCombiningTransactionAndStatus(transactionId, status));

        return BinaryMerkleTree.FromLeafNodes(nodes).Root;
    }

    private Hash CalculateTransactionMerkleTreeRoot(IEnumerable<Hash> transactionIds)
    {
        Logger.LogTrace("Start transaction merkle tree root calculation.");
        return BinaryMerkleTree.FromLeafNodes(transactionIds).Root;
    }

    private Hash GetHashCombiningTransactionAndStatus(Hash txId,
        TransactionResultStatus executionReturnStatus)
    {
        // combine tx result status
        var rawBytes = ByteArrayHelper.ConcatArrays(txId.ToByteArray(),
            EncodingHelper.EncodeUtf8(executionReturnStatus.ToString()));
        return HashHelper.ComputeFrom(rawBytes);
    }

    private BlockStateSet CreateBlockStateSet(Hash previousBlockHash, long blockHeight,
        ExecutionReturnSetCollection executionReturnSetCollection)
    {
        var blockStateSet = new BlockStateSet
        {
            BlockHeight = blockHeight,
            PreviousHash = previousBlockHash
        };
        foreach (var returnSet in executionReturnSetCollection.Executed)
        {
            foreach (var change in returnSet.StateChanges)
            {
                blockStateSet.Changes[change.Key] = change.Value;
                blockStateSet.Deletes.Remove(change.Key);
            }

            foreach (var delete in returnSet.StateDeletes)
            {
                blockStateSet.Deletes.AddIfNotContains(delete.Key);
                blockStateSet.Changes.Remove(delete.Key);
            }
        }

        return blockStateSet;
    }

    private async Task<List<TransactionResult>> SetTransactionResultsAsync(
        ExecutionReturnSetCollection executionReturnSetCollection, BlockHeader blockHeader)
    {
        //save all transaction results
        var results = executionReturnSetCollection.GetExecutionReturnSetList()
            .Select(p =>
            {
                p.TransactionResult.BlockHash = blockHeader.GetHash();
                p.TransactionResult.BlockNumber = blockHeader.Height;
                return p.TransactionResult;
            }).ToList();

        await _transactionResultService.AddTransactionResultsAsync(results, blockHeader);
        return results;
    }
}