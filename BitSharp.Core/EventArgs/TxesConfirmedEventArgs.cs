﻿using BitSharp.Common;
using BitSharp.Core.Domain;
using System;
using System.Collections.Immutable;

namespace BitSharp.Core
{
    public class TxesConfirmedEventArgs : EventArgs
    {
        public TxesConfirmedEventArgs(ChainedHeader confirmBlock, ImmutableDictionary<UInt256, UnconfirmedTx> confirmedTxes)
        {
            ConfirmBlock = confirmBlock;
            ConfirmedTxes = confirmedTxes;
        }

        public ChainedHeader ConfirmBlock { get; }

        public ImmutableDictionary<UInt256, UnconfirmedTx> ConfirmedTxes { get; }
    }
}
