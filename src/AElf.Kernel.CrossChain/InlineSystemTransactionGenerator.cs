using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Blockchain.Domain;
using AElf.Kernel.Miner.Application;
using AElf.Kernel.SmartContract.Application;
using AElf.Standards.ACS7;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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

        if (inlineTransactionInfo == null || inlineTransactionInfo.List.IsNullOrEmpty())
            return generatedTransactions;

        var crossChainContractAddress = await _smartContractAddressService.GetAddressByContractNameAsync(
            chainContext, CrossChainSmartContractAddressNameProvider.StringName);

        if (crossChainContractAddress == null) return generatedTransactions;

        var generatedTransaction = new Transaction
        {
            From = from,
            MethodName = nameof(ACS7Container.ACS7Stub.GetChainInitializationData),
            To = crossChainContractAddress,
            RefBlockNumber = preBlockHeight,
            RefBlockPrefix = BlockHelper.GetRefBlockPrefix(preBlockHash),
            Params = new Empty().ToByteString()
        };
        generatedTransactions.Add(generatedTransaction);

        Logger.LogTrace("Inline cross chain system transaction generated.");

        return generatedTransactions;
    }

    private async Task Test(Hash preBlockHash)
    {
        var block = await _blockchainService.GetBlockByHashAsync(preBlockHash);
        var transactions =
            await _transactionResultManager.GetTransactionResultsAsync(block.TransactionIds.ToList(), preBlockHash);

        var inlineLogs = new List<LogEvent>();
        foreach (var tx in transactions)
        {
            inlineLogs.AddRange(tx.Logs.Where(log => log.Name.Contains("Inline")));
        }

        await _inlineTransactionProvider.SetInlineTransactionInfoAsync(new BlockIndex
        {
            BlockHash = preBlockHash
        }, new InlineTransactionInfo());
    }
}