using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Blockchain.Domain;
using AElf.Kernel.Miner.Application;
using AElf.Kernel.SmartContract.Application;
using AElf.Standards.ACS0;
using AElf.Standards.ACS7;
using AElf.Types;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AElf.Kernel.CrossChain;

public class InlineSystemTransactionGenerator : ISystemTransactionGenerator
{
    private readonly ISmartContractAddressService _smartContractAddressService;
    private readonly IInlineTransactionProvider _inlineTransactionProvider;
    private readonly IBlockchainService _blockchainService;
    private readonly ITransactionResultManager _transactionResultManager;

    public InlineSystemTransactionGenerator(ISmartContractAddressService smartContractAddressService,
        IInlineTransactionProvider inlineTransactionProvider, IBlockchainService blockchainService,
        ITransactionResultManager transactionResultManager)
    {
        _smartContractAddressService = smartContractAddressService;
        _inlineTransactionProvider = inlineTransactionProvider;
        _blockchainService = blockchainService;
        _transactionResultManager = transactionResultManager;

        Logger = NullLogger<InlineSystemTransactionGenerator>.Instance;
    }

    public ILogger<InlineSystemTransactionGenerator> Logger { get; set; }

    public async Task<List<Transaction>> GenerateTransactionsAsync(Address from, long preBlockHeight, Hash preBlockHash)
    {
        var generatedTransactions = new List<Transaction>();
        var chainContext = new ChainContext
        {
            BlockHash = preBlockHash, BlockHeight = preBlockHeight
        };

        var inlineTransactionInfo = await _inlineTransactionProvider.GetInlineTransactionInfoAsync(chainContext);

        if (inlineTransactionInfo == null || inlineTransactionInfo.TransactionIds.IsNullOrEmpty())
            return generatedTransactions;

        var crossChainContractAddress = await _smartContractAddressService.GetAddressByContractNameAsync(
            chainContext, CrossChainSmartContractAddressNameProvider.StringName);

        if (crossChainContractAddress == null) return generatedTransactions;

        var info = await Test(preBlockHeight, preBlockHash);
        if (info.TransactionIds.IsNullOrEmpty())
        {
            return generatedTransactions;
        }

        var generatedTransaction = new Transaction
        {
            From = from,
            MethodName = nameof(ACS7Container.ACS7Stub.IndexTest),
            To = crossChainContractAddress,
            RefBlockNumber = preBlockHeight,
            RefBlockPrefix = BlockHelper.GetRefBlockPrefix(preBlockHash),
            Params = new IndexTestInput
            {
                MerkleTreeRoot = info.MerkleTreeRootOfInlineTransactions
            }.ToByteString()
        };
        generatedTransactions.Add(generatedTransaction);

        Logger.LogTrace("Inline cross chain system transaction generated.");
        Logger.LogDebug($"123454321 System Transaction Id: {generatedTransaction.GetHash().ToHex()}");

        return generatedTransactions;
    }

    private async Task<InlineTransactionInfo> Test(long preBlockHeight, Hash preBlockHash)
    {
        var block = await _blockchainService.GetBlockByHashAsync(preBlockHash);
        var transactions =
            await _transactionResultManager.GetTransactionResultsAsync(block.TransactionIds.ToList(), preBlockHash);

        var infos = new Dictionary<Hash, Transaction>();
        foreach (var tx in transactions)
        {
            var logs = tx.Logs.Where(t => t.Name.Contains("Inline")).ToList();
            if (!logs.IsNullOrEmpty())
            {
                var index = 0;
                foreach (var log in logs)
                {
                    var inlineLogEvent = InlineLogEvent.Parser.ParseFrom(log.NonIndexed);
                    if (inlineLogEvent.MethodName.Contains("Test"))
                    {
                        var transaction = new Transaction
                        {
                            From = inlineLogEvent.From,
                            To = inlineLogEvent.To,
                            MethodName = inlineLogEvent.MethodName,
                            Params = inlineLogEvent.Params
                        };
                        var id = HashHelper.ConcatAndCompute(
                            HashHelper.ConcatAndCompute(tx.TransactionId, transaction.GetHash()),
                            HashHelper.ComputeFrom(index.ToString()));
                        Logger.LogDebug($"123454321 index: {index}, id: {id.ToHex()}, block hash: {preBlockHash}, block height: {preBlockHeight}");
                        infos.Add(id, transaction);
                        index++;
                    }
                }
            }
        }

        var inlineTransactionInfo = new InlineTransactionInfo();
        if (!infos.IsNullOrEmpty())
        {
            var merkleTreeRootOfInlineTransactions = BinaryMerkleTree.FromLeafNodes(infos.Select(t => GetHashCombiningTransactionAndStatus(t.Key, TransactionResultStatus.Mined))).Root;

            inlineTransactionInfo = new InlineTransactionInfo
            {
                TransactionIds = infos,
                MerkleTreeRootOfInlineTransactions = merkleTreeRootOfInlineTransactions
            };

            await _inlineTransactionProvider.SetInlineTransactionInfoAsync(new BlockIndex
            {
                BlockHash = preBlockHash,
                BlockHeight = preBlockHeight
            }, inlineTransactionInfo);
        }
        
        return inlineTransactionInfo;
    }
    
    private Hash GetHashCombiningTransactionAndStatus(Hash txId,
        TransactionResultStatus executionReturnStatus)
    {
        // combine tx result status
        var rawBytes = txId.ToByteArray().Concat(Encoding.UTF8.GetBytes(executionReturnStatus.ToString()))
            .ToArray();
        return HashHelper.ComputeFrom(rawBytes);
    }
}