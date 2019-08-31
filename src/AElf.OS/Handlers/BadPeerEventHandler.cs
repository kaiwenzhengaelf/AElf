using System.Threading.Tasks;
using AElf.OS.BlockSync.Events;
using AElf.OS.Network.Application;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;

namespace AElf.OS.Handlers
{
    public class BadPeerEventHandler : ILocalEventHandler<BlockValidationFailedEventData>, ITransientDependency
    {
        private readonly INetworkService _networkService;

        public BadPeerEventHandler(INetworkService networkService)
        {
            _networkService = networkService;
        }

        public Task HandleEventAsync(BlockValidationFailedEventData eventData)
        {
            HandleBadPeerAsync(eventData.BlockSenderPubkey);
            return Task.CompletedTask;
        }

        private async Task HandleBadPeerAsync(string peerPubkey)
        {
            await _networkService.RemovePeerByPubkeyAsync(peerPubkey);
        }
    }
}