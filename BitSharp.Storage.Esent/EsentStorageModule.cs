﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Data;
using Microsoft.Isam.Esent.Collections.Generic;
using Microsoft.Isam.Esent.Interop;
using Ninject;
using Ninject.Modules;
using Ninject.Parameters;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitSharp.Storage.Esent
{
    public class EsentStorageModule : NinjectModule
    {
        private readonly string baseDirectory;
        private readonly long cacheSizeMaxBytes;

        public EsentStorageModule(string baseDirectory, long cacheSizeMaxBytes)
        {
            this.baseDirectory = baseDirectory;
            this.cacheSizeMaxBytes = cacheSizeMaxBytes;
        }

        public override void Load()
        {
            var esentAssembly = typeof(PersistentDictionary<string, string>).Assembly;
            var type = esentAssembly.GetType("Microsoft.Isam.Esent.Collections.Generic.CollectionsSystemParameters");
            var method = type.GetMethod("Init");
            method.Invoke(null, null);
            SystemParameters.CacheSizeMax = (cacheSizeMaxBytes / SystemParameters.DatabasePageSize).ToIntChecked();

            // bind concrete storage providers
            this.Bind<BlockHeaderStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);
            this.Bind<ChainedBlockStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);
            this.Bind<BlockTxHashesStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);
            this.Bind<TransactionStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);
            this.Bind<BlockRollbackStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);
            this.Bind<InvalidBlockStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);
            this.Bind<NetworkPeerStorage>().ToSelf().InSingletonScope().WithConstructorArgument("baseDirectory", this.baseDirectory);

            // bind storage providers interfaces
            this.Bind<IBlockHeaderStorage>().ToMethod(x => this.Kernel.Get<BlockHeaderStorage>()).InSingletonScope();
            this.Bind<IChainedBlockStorage>().ToMethod(x => this.Kernel.Get<ChainedBlockStorage>()).InSingletonScope();
            this.Bind<IBlockTxHashesStorage>().ToMethod(x => this.Kernel.Get<MemoryBlockTxHashesStorage>()).InSingletonScope();
            this.Bind<ITransactionStorage>().ToMethod(x => this.Kernel.Get<MemoryTransactionStorage>()).InSingletonScope();
            this.Bind<IBlockRollbackStorage>().ToMethod(x => this.Kernel.Get<BlockRollbackStorage>()).InSingletonScope();
            this.Bind<IInvalidBlockStorage>().ToMethod(x => this.Kernel.Get<InvalidBlockStorage>()).InSingletonScope();
            this.Bind<INetworkPeerStorage>().ToMethod(x => this.Kernel.Get<NetworkPeerStorage>()).InSingletonScope();
            
            this.Bind<IUtxoBuilderStorage>().To<UtxoBuilderStorage>();
        }
    }
}
