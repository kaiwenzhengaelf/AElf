using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.SmartContract.Infrastructure;
using AElf.Standards.ACS0;
using AElf.Types;
using Google.Protobuf;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.Application;

public interface ISmartContractAddressService
{
    Task<Address> GetAddressByContractNameAsync(IChainContext chainContext, string name);
    Task<SmartContractAddressDto> GetSmartContractAddressAsync(IChainContext chainContext, string name);
    Task SetSmartContractAddressAsync(IBlockIndex blockIndex, string name, Address address);

    Address GetZeroSmartContractAddress();

    Address GetZeroSmartContractAddress(int chainId);

    Task<IReadOnlyDictionary<Hash, Address>> GetSystemContractNameToAddressMappingAsync(IChainContext chainContext);

    Task SetSmartContractNameAsync(IBlockIndex blockIndex, string address, Hash name);
    Task<SmartContractNameDto> GetSmartContractNameAsync(IChainContext chainContext, string address);
}

public class SmartContractAddressService : ISmartContractAddressService, ISingletonDependency
{
    private readonly IBlockchainService _blockchainService;
    private readonly IDefaultContractZeroCodeProvider _defaultContractZeroCodeProvider;
    private readonly IEnumerable<ISmartContractAddressNameProvider> _smartContractAddressNameProviders;
    private readonly ISmartContractAddressProvider _smartContractAddressProvider;
    private readonly ISmartContractNameProvider _smartContractNameProvider;
    private readonly ITransactionReadOnlyExecutionService _transactionReadOnlyExecutionService;

    public SmartContractAddressService(IDefaultContractZeroCodeProvider defaultContractZeroCodeProvider,
        ITransactionReadOnlyExecutionService transactionReadOnlyExecutionService,
        ISmartContractAddressProvider smartContractAddressProvider,
        IEnumerable<ISmartContractAddressNameProvider> smartContractAddressNameProviders,
        IBlockchainService blockchainService, ISmartContractNameProvider smartContractNameProvider)
    {
        _defaultContractZeroCodeProvider = defaultContractZeroCodeProvider;
        _transactionReadOnlyExecutionService = transactionReadOnlyExecutionService;
        _smartContractAddressProvider = smartContractAddressProvider;
        _smartContractAddressNameProviders = smartContractAddressNameProviders;
        _blockchainService = blockchainService;
        _smartContractNameProvider = smartContractNameProvider;
    }

    public async Task<Address> GetAddressByContractNameAsync(IChainContext chainContext, string name)
    {
        var smartContractAddress = await _smartContractAddressProvider.GetSmartContractAddressAsync(chainContext, name);
        var address = smartContractAddress?.Address;
        if (address == null) address = await GetSmartContractAddressFromStateAsync(chainContext, name);
        return address;
    }

    public async Task<SmartContractAddressDto> GetSmartContractAddressAsync(IChainContext chainContext, string name)
    {
        var smartContractAddress =
            await _smartContractAddressProvider.GetSmartContractAddressAsync(chainContext, name);
        if (smartContractAddress != null)
        {
            var smartContractAddressDto = new SmartContractAddressDto
            {
                SmartContractAddress = smartContractAddress,
                Irreversible = await CheckSmartContractAddressIrreversibleAsync(smartContractAddress)
            };

            return smartContractAddressDto;
        }

        var address = await GetSmartContractAddressFromStateAsync(chainContext, name);
        if (address == null) return null;
        return new SmartContractAddressDto
        {
            SmartContractAddress = new SmartContractAddress
            {
                Address = address
            }
        };
    }

    public virtual async Task SetSmartContractAddressAsync(IBlockIndex blockIndex, string name, Address address)
    {
        await _smartContractAddressProvider.SetSmartContractAddressAsync(blockIndex, name, address);
    }
    
    public async Task<SmartContractNameDto> GetSmartContractNameAsync(IChainContext chainContext, string address)
    {
        var smartContractName =
            await _smartContractNameProvider.GetSmartContractNameAsync(chainContext, address);
        if (smartContractName != null)
        {
            var smartContractNameDto = new SmartContractNameDto
            {
                SmartContractName = smartContractName,
                Irreversible = await CheckSmartContractNameIrreversibleAsync(smartContractName)
            };

            return smartContractNameDto;
        }

        var name = await GetSmartContractNameFromStateAsync(chainContext, address);
        if (name == null) return null;
        return new SmartContractNameDto
        {
            SmartContractName = new SmartContractName
            {
                Name = name
            }
        };
    }

    public virtual async Task SetSmartContractNameAsync(IBlockIndex blockIndex, string address, Hash name)
    {
        await _smartContractNameProvider.SetSmartContractNameAsync(blockIndex, address, name);
    }
    
    public Address GetZeroSmartContractAddress()
    {
        return _defaultContractZeroCodeProvider.ContractZeroAddress;
    }

    public Address GetZeroSmartContractAddress(int chainId)
    {
        return _defaultContractZeroCodeProvider.GetZeroSmartContractAddress(chainId);
    }

    public virtual async Task<IReadOnlyDictionary<Hash, Address>> GetSystemContractNameToAddressMappingAsync(
        IChainContext chainContext)
    {
        var map = new Dictionary<Hash, Address>();
        foreach (var smartContractAddressNameProvider in _smartContractAddressNameProviders)
        {
            var address =
                await GetAddressByContractNameAsync(chainContext, smartContractAddressNameProvider.ContractStringName);
            if (address != null)
                map[smartContractAddressNameProvider.ContractName] = address;
        }

        return new ReadOnlyDictionary<Hash, Address>(map);
    }

    private async Task<bool> CheckSmartContractAddressIrreversibleAsync(SmartContractAddress smartContractAddress)
    {
        var chain = await _blockchainService.GetChainAsync();
        if (smartContractAddress.BlockHeight > chain.LastIrreversibleBlockHeight) return false;

        var blockHash = await _blockchainService.GetBlockHashByHeightAsync(chain,
            smartContractAddress.BlockHeight, chain.LastIrreversibleBlockHash);
        return blockHash == smartContractAddress.BlockHash;
    }
    
    private async Task<bool> CheckSmartContractNameIrreversibleAsync(SmartContractName smartContractName)
    {
        var chain = await _blockchainService.GetChainAsync();
        if (smartContractName.BlockHeight > chain.LastIrreversibleBlockHeight) return false;

        var blockHash = await _blockchainService.GetBlockHashByHeightAsync(chain,
            smartContractName.BlockHeight, chain.LastIrreversibleBlockHash);
        return blockHash == smartContractName.BlockHash;
    }

    private async Task<Address> GetSmartContractAddressFromStateAsync(IChainContext chainContext, string name)
    {
        var zeroAddress = _defaultContractZeroCodeProvider.ContractZeroAddress;
        var tx = new Transaction
        {
            From = zeroAddress,
            To = zeroAddress,
            MethodName = nameof(ACS0Container.ACS0Stub.GetContractAddressByName),
            Params = Hash.LoadFromBase64(name).ToByteString()
        };
        var address = await _transactionReadOnlyExecutionService.ExecuteAsync<Address>(
            chainContext, tx, TimestampHelper.GetUtcNow(), false);

        return address == null || address.Value.IsEmpty ? null : address;
    }
    
    private async Task<Hash> GetSmartContractNameFromStateAsync(IChainContext chainContext, string address)
    {
        var zeroAddress = _defaultContractZeroCodeProvider.ContractZeroAddress;
        var tx = new Transaction
        {
            From = zeroAddress,
            To = zeroAddress,
            MethodName = nameof(ACS0Container.ACS0Stub.GetContractInfo),
            Params = Address.FromBase58(address).ToByteString()
        };
        var info = await _transactionReadOnlyExecutionService.ExecuteAsync<ContractInfo>(
            chainContext, tx, TimestampHelper.GetUtcNow(), false);

        return info == null || info.ContractName == null || info.ContractName.Value.IsNullOrEmpty() ? null : info.ContractName;
    }
}

public class SmartContractAddressDto
{
    public SmartContractAddress SmartContractAddress { get; set; }
    public bool Irreversible { get; set; }
}

public class SmartContractNameDto
{
    public SmartContractName SmartContractName { get; set; }
    public bool Irreversible { get; set; }
}