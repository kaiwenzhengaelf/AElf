using AElf.Contracts.Election;
using AElf.Contracts.Profit;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.TestContract.VirtualAddress;

public partial class Action : VirtualAddressContractContainer.VirtualAddressContractBase
{
    public override Empty VirtualAddressVote(VirtualAddressVoteInput input)
    {
        Initialize();

        Context.SendVirtualInline(HashHelper.ComputeFrom("test"), State.ElectionContract.Value, "Vote", new VoteMinerInput
        {
            CandidatePubkey = input.PubKey,
            Amount = input.Amount,
            EndTimestamp = input.EndTimestamp,
            Token = input.Token
        }.ToByteString());

        return new Empty();
    }
    
    public override Empty VirtualAddressWithdraw(Hash input)
    {
        Initialize();

        Context.SendVirtualInline(HashHelper.ComputeFrom("test"), State.ElectionContract.Value, "Withdraw", input.ToByteString());

        return new Empty();
    }
    
    public override Empty VirtualAddressChangeVotingOption(VirtualAddressChangeVotingOptionInput input)
    {
        Initialize();

        Context.SendVirtualInline(HashHelper.ComputeFrom("test"), State.ElectionContract.Value, "ChangeVotingOption", new ChangeVotingOptionInput
        {
            CandidatePubkey = input.PubKey,
            VoteId = input.VoteId,
            IsResetVotingTime = input.IsReset
        }.ToByteString());

        return new Empty();
    }

    public override Empty VirtualAddressClaimProfit(VirtualAddressClaimProfitInput input)
    {
        Initialize();

        Context.SendVirtualInline(HashHelper.ComputeFrom("test"), State.ProfitContract.Value, "ClaimProfits", new ClaimProfitsInput
        {
            SchemeId = input.SchemeId,
            Beneficiary = input.Beneficiary
        }.ToByteString());

        return new Empty();
    }
    
    public override Empty ForwardCall(ForwardCallInput input)
    {
        Context.SendVirtualInline(input.VirtualAddress, input.ContractAddress, input.MethodName, input.Args);
        return new Empty();
    }

    public override Address GetVirtualAddress(Empty input)
    {
        return Context.ConvertVirtualAddressToContractAddress(HashHelper.ComputeFrom("test"));
    }

    private void Initialize()
    {
        if (State.ElectionContract.Value == null)
        {
            State.ElectionContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ElectionContractSystemName);
        }
        
        if (State.ProfitContract.Value == null)
        {
            State.ProfitContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ProfitContractSystemName);
        }
    }

    public override Empty Test(Empty input)
    {
        var originNext = Context.OriginNext;
        Context.SendVirtualInline(HashHelper.ComputeFrom("test"), Context.Self, "Test2", new Empty().ToByteString());

        return new Empty();
    }

    public override Empty Test2(Empty input)
    {
        var originNext = Context.OriginNext;
        Context.SendVirtualInline(HashHelper.ComputeFrom("test2"), Context.Self, "Test3", new Empty().ToByteString());

        return new Empty();
    }
    
    public override Empty Test3(Empty input)
    {
        var originNext = Context.OriginNext;

        return new Empty();
    }
}