using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using AElf.Kernel.Storages;

namespace AElf.Kernel
{
    public class AccountDataProvider : IAccountDataProvider
    {
        private readonly IPointerCollection _pointerCollection;
        private readonly IWorldStateManager _worldStateManager;
        private readonly Hash _preBlockHash;
        private readonly IChangesCollection _changesCollection;
        
        public IAccountDataContext Context { get; set; }

        public AccountDataProvider(Hash accountHash, Hash chainId, 
            IAccountContextService accountContextService, IPointerCollection pointerCollection,
            IWorldStateManager worldStateManager, Hash preBlockHash, IChangesCollection changesCollection)
        {
            _worldStateManager = worldStateManager;
            _preBlockHash = preBlockHash;
            _changesCollection = changesCollection;
            _pointerCollection = pointerCollection;
            Context = accountContextService.GetAccountDataContext(accountHash, chainId, false);
        }

        public IDataProvider GetDataProvider()
        {
            return new DataProvider(Context, _pointerCollection, _worldStateManager, _preBlockHash, _changesCollection);
        }
    }
}
