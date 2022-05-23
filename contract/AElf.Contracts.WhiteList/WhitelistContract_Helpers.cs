using System.Linq;
using AElf.Contracts.Whitelist.Extensions;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Whitelist
{
    public partial class WhitelistContract
    {

        private Hash CalculateWhitelistHash(Address address,Hash input)
        {
            return Context.GenerateId(Context.Self, ByteArrayHelper.ConcatArrays(address.ToByteArray(),input.ToByteArray()));
        }

        private Hash CalculateSubscribeWhitelistHash(Address address,Hash projectId,Hash whitelistId)
        {
            return HashHelper.ComputeFrom($"{address}{projectId}{whitelistId}");
        }

        private Hash CalculateCloneWhitelistHash(Address address,Hash whitelistId)
        {
            return HashHelper.ComputeFrom($"{address}{whitelistId}");
        }
        
        private WhitelistInfo AssertWhitelistInfo(Hash whitelistId)
        {
            var whitelistInfo = State.WhitelistInfoMap[whitelistId];
            Assert(whitelistInfo != null,$"Whitelist not found.{whitelistId.ToHex()}");
            return whitelistInfo;
        }
        
        private WhitelistInfo AssertWhitelistIsAvailable(Hash whitelistId)
        {
            var whitelistInfo = State.WhitelistInfoMap[whitelistId];
            Assert(whitelistInfo.IsAvailable, $"Whitelist is not available.{whitelistId.ToHex()}");
            return whitelistInfo;
        }

        private WhitelistInfo AssertWhitelistManager(Hash whitelistId)
        {
            var whitelistInfo = GetWhitelist(whitelistId);
            Assert(whitelistInfo.Manager.Value.Contains(Context.Sender),$"{Context.Sender} is not the manager of the whitelist.");
            return whitelistInfo;
        }

        private WhitelistInfo AssertWhitelistCreator(Hash whitelistId)
        {
            var whitelistInfo = GetWhitelist(whitelistId);
            Assert(whitelistInfo.Creator == Context.Sender,$"{Context.Sender}No permission.");
            return whitelistInfo;
        }
        

        private SubscribeWhitelistInfo AssertSubscribeWhitelistInfo(Hash subscribeId)
        {
            var subscribeInfo = State.SubscribeWhitelistInfoMap[subscribeId];
            Assert(subscribeInfo != null, $"Subscribe info not found.{subscribeId.ToHex()}");
            return subscribeInfo;
        }

        private ExtraInfoId AssertExtraInfoDuplicate(Hash whitelistId, ExtraInfoId id)
        {
            var whitelist = State.WhitelistInfoMap[whitelistId];
            var addressList = whitelist.ExtraInfoIdList.Value.Select(e => e.Address).ToList();
            var ifAddressDuplicate = addressList.Contains(id.Address);
            Assert(!ifAddressDuplicate,$"Duplicate address.");
            var ifExist = whitelist.ExtraInfoIdList.Value.Contains(id);
            Assert(!ifExist, $"ExtraInfo already exists.{whitelistId}{id}");
            return id;
        }
        
        private ExtraInfoId AssertExtraInfoIsNotExist(Hash subscribeId, ExtraInfoId info)
        {
            var whitelist = GetAvailableWhitelist(subscribeId);
            var ifExist = whitelist.Value.Contains(ConvertToInfoList(new ExtraInfoIdList(){Value = { info }}).Value[0]);
            Assert(ifExist, $"ExtraInfo doesn't exist in the available whitelist.{info}");
            return info;
        }
        
        private ExtraInfo ConvertToInfo(ExtraInfoId extraInfoId)
        {
            var extraInfo = State.TagInfoMap[extraInfoId.Id];
            return new ExtraInfo()
            {
                Address = extraInfoId.Address,
                Info = new TagInfo()
                {
                    TagName = extraInfo.TagName,
                    Info = extraInfo.Info
                }
            };
        }
        
        private ExtraInfoList ConvertToInfoList(ExtraInfoIdList extraInfoIdList)
        {
            var extraInfo = extraInfoIdList.Value.Select(e =>
            {
                var infoId = e.Id;
                var info = State.TagInfoMap[infoId];
                return new ExtraInfo()
                {
                    Address = e.Address,
                    Info = new TagInfo()
                    {
                        TagName = info.TagName,
                        Info = info.Info
                    }
                };
            }).ToList();
            return new ExtraInfoList() { Value = {extraInfo} };
        }
        
        /// <summary>
        /// Create TagInfo when creating whitelist with tagInfo.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="projectId"></param>
        /// <returns></returns>
        /// <exception cref="AssertionException"></exception>
        private Hash CreateTagInfo(TagInfo info,Hash projectId)
        {
            if (info == null)
            {
                throw new AssertionException("TagInfo is null.");
            }
            var id = Context.Sender.CalculateExtraInfoId(projectId,info.TagName);
            if (State.TagInfoMap[id] != null) return id;
            State.TagInfoMap[id] = info;
            Context.Fire(new TagInfoAdded()
            {
                TagInfoId = id,
                TagInfo = State.TagInfoMap[id]
            });
            return id;
        }

        // /// <summary>
        // ///remove address or address+extra_info
        // /// </summary>
        // /// <returns>AddressExtraIdInfo</returns>
        // private ExtraInfoId RemoveAddressOrExtra(WhitelistInfo whitelistInfo, ExtraInfoId extraInfoId)
        // {
        //     if (extraInfoId.Id.Value.IsEmpty)
        //     {
        //         var address = extraInfoId.Address;
        //         var resultList = whitelistInfo.ExtraInfoIdList.Value
        //             .Where(u => u.Address.Equals(address)).ToList();
        //         Assert(resultList.Count == 1 , $"Address doesn't exist.{extraInfoId.Address}");
        //         foreach (var result in resultList)
        //         {
        //             whitelistInfo.ExtraInfoIdList.Value.Remove(result);
        //         }
        //         State.WhitelistInfoMap[whitelistInfo.WhitelistId] = whitelistInfo;
        //         return new ExtraInfoId()
        //         {
        //             Address = address
        //         };
        //     }
        //     var toRemove = whitelistInfo.ExtraInfoIdList.Value
        //         .Where(u => u.Address == extraInfoId.Address && u.Id == extraInfoId.Id)
        //         .ToList();
        //     Assert(toRemove.Count == 1, $"Address and tag info doesn't exist.{extraInfoId}");
        //     foreach (var result in toRemove)
        //     {
        //         whitelistInfo.ExtraInfoIdList.Value.Remove(result);
        //     }
        //     State.WhitelistInfoMap[whitelistInfo.WhitelistId] = whitelistInfo;
        //     return extraInfoId;
        // }
        
        private AddressList SetManagerList(Hash whitelistId,AddressList input)
        {
            var managerList = input ?? new AddressList();
            if (!managerList.Value.Contains(Context.Sender))
            {
                managerList.Value.Add(Context.Sender);
            }
            State.ManagerListMap[whitelistId] = managerList;
            return State.ManagerListMap[whitelistId];
        }

        private void SetWhitelistIdManager(Hash whitelistId,AddressList managerList)
        {
            foreach (var manager in managerList.Value)
            {
                var whitelistIdList = State.WhitelistIdMap[manager] ?? new WhitelistIdList();
                whitelistIdList.WhitelistId.Add(whitelistId);
                State.WhitelistIdMap[manager] = whitelistIdList;
            }
        }
        
        private void RemoveWhitelistIdManager(Hash whitelistId,AddressList managerList)
        {
            foreach (var manager in managerList.Value)
            {
                var whitelistIdList = State.WhitelistIdMap[manager] ?? new WhitelistIdList();
                whitelistIdList.WhitelistId.Remove(whitelistId);
                State.WhitelistIdMap[manager] = whitelistIdList;
            }
        }
    }
}