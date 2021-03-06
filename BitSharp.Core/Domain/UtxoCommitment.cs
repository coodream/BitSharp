﻿using BitSharp.Common;
using System.Security.Cryptography;

namespace BitSharp.Core.Domain
{
    public static class UtxoCommitment
    {
        public static UInt256 ComputeHash(IChainState chainState)
        {
            using (var sha256 = new SHA256Managed())
            {
                // add each unspent tx to hash
                foreach (var unspentTx in chainState.ReadUnspentTransactions())
                {
                    var unspentTxBytes = DataEncoder.EncodeUnspentTx(unspentTx);
                    sha256.TransformBlock(unspentTxBytes, 0, unspentTxBytes.Length, unspentTxBytes, 0);
                }
                // finalize hash
                sha256.TransformFinalBlock(new byte[0], 0, 0);

                // hash again to return double-hashed utxo committment
                return new UInt256(SHA256Static.ComputeHash(sha256.Hash));
            }
        }
    }
}
