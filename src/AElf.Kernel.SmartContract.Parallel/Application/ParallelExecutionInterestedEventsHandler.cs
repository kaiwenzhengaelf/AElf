using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Events;
using AElf.Kernel.SmartContractExecution.Application;
using AElf.Kernel.TransactionPool;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;

namespace AElf.Kernel.SmartContract.Parallel
{
    public class ParallelExecutionInterestedEventsHandler : ILocalEventHandler<TransactionAcceptedEvent>,
        ILocalEventHandler<NewIrreversibleBlockFoundEvent>,
        ILocalEventHandler<BlockAcceptedEvent>,
        ITransientDependency
    {
        private readonly IResourceExtractionService _resourceExtractionService;
        private readonly ITaskQueueManager _taskQueueManager;

        public ParallelExecutionInterestedEventsHandler(IResourceExtractionService resourceExtractionService,
            ITaskQueueManager taskQueueManager)
        {
            _resourceExtractionService = resourceExtractionService;
            _taskQueueManager = taskQueueManager;
        }

        public async Task HandleEventAsync(TransactionAcceptedEvent eventData)
        {
            await _resourceExtractionService.HandleTransactionAcceptedEvent(eventData);
            // return Task.CompletedTask;
        }

        public async Task HandleEventAsync(BlockAcceptedEvent eventData)
        {
            await _resourceExtractionService.HandleBlockAcceptedAsync(eventData);
        }

        public Task HandleEventAsync(NewIrreversibleBlockFoundEvent eventData)
        {
            _taskQueueManager.Enqueue(
                async () => { await _resourceExtractionService.HandleNewIrreversibleBlockFoundAsync(eventData); },
                KernelConstants.ChainCleaningQueueName);
            
            return Task.CompletedTask;
        }
    }
}
