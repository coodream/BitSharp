﻿using BitSharp.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitSharp.Storage
{
    public interface IStorageContext : IDisposable
    {
        IBlockHeaderStorage BlockHeaderStorage { get; }

        IBlockTxHashesStorage BlockTxHashesStorage { get; }

        ITransactionStorage TransactionStorage { get; }

        IChainedBlockStorage ChainedBlockStorage { get; }

        //IBlockchainStorage BlockchainStorage { get; }

        IEnumerable<ChainedBlock> SelectMaxTotalWorkBlocks();
        
        IUtxoBuilderStorage ToUtxoBuilder(Utxo utxo);
    }
}
