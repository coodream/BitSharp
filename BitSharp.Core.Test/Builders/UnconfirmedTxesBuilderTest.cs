﻿using BitSharp.Common;
using BitSharp.Core.Builders;
using BitSharp.Core.Domain;
using BitSharp.Core.Test.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BitSharp.Core.Storage;
using Moq;

namespace BitSharp.Core.Test.Builders
{
    [TestClass]
    public class UnconfirmedTxesBuilderTest
    {
        // TODO run as a standard StorageProviderTest
        private MemoryTestStorageProvider storageProvider;
        private IStorageManager storageManager;

        [TestInitialize]
        public void Initialize()
        {
            storageProvider = new MemoryTestStorageProvider();
            storageManager = storageProvider.OpenStorageManager();
        }

        [TestCleanup]
        public void Cleanup()
        {
            storageProvider?.TestCleanup();
            storageManager?.Dispose();

            storageProvider = null;
            storageManager = null;
        }

        [TestMethod]
        public void TestUnconfTxAdded()
        {
            // create tx spending a previous output that exists
            var tx = Transaction.Create(
                0,
                ImmutableArray.Create(new TxInput(UInt256.One, 0, ImmutableArray<byte>.Empty, 0)),
                ImmutableArray.Create(new TxOutput(0, ImmutableArray<byte>.Empty)),
                0).Transaction;

            // create prev output tx
            var unspentTx = new UnspentTx(tx.Inputs[0].PrevTxHash, 0, 1, 0, false, new OutputStates(1, OutputState.Unspent), ImmutableArray<TxOutput>.Empty);

            // mock chain state with prev output
            var chainState = new Mock<IChainState>();
            chainState.Setup(x => x.TryGetUnspentTx(tx.Inputs[0].PrevTxHash, out unspentTx)).Returns(true);

            // mock core daemon for chain state retrieval
            var coreDaemon = new Mock<ICoreDaemon>();
            coreDaemon.Setup(x => x.GetChainState()).Returns(chainState.Object);

            using (var unconfirmedTxesBuilder = new UnconfirmedTxesBuilder(coreDaemon.Object, storageManager))
            {
                // try to add the tx
                Assert.IsTrue(unconfirmedTxesBuilder.TryAddTransaction(tx));

                // verify unconfirmed tx was added
                UnconfirmedTx unconfirmedTx;
                Assert.IsTrue(unconfirmedTxesBuilder.TryGetTransaction(tx.Hash, out unconfirmedTx));
                Assert.IsNotNull(unconfirmedTx);
            }
        }

        [TestMethod]
        public void TestUnconfTxMissingPrevOutput()
        {
            // create tx spending a previous output that doesn't exist
            var tx = Transaction.Create(
                0,
                ImmutableArray.Create(new TxInput(UInt256.One, 0, ImmutableArray<byte>.Empty, 0)),
                ImmutableArray.Create(new TxOutput(0, ImmutableArray<byte>.Empty)),
                0).Transaction;

            // mock empty chain state
            var chainState = new Mock<IChainState>();

            // mock core daemon for chain state retrieval
            var coreDaemon = new Mock<ICoreDaemon>();
            coreDaemon.Setup(x => x.GetChainState()).Returns(chainState.Object);

            using (var unconfirmedTxesBuilder = new UnconfirmedTxesBuilder(coreDaemon.Object, storageManager))
            {
                // try to add the tx
                Assert.IsFalse(unconfirmedTxesBuilder.TryAddTransaction(tx));

                // verify unconfirmed tx was not added
                UnconfirmedTx unconfirmedTx;
                Assert.IsFalse(unconfirmedTxesBuilder.TryGetTransaction(tx.Hash, out unconfirmedTx));
                Assert.IsNull(unconfirmedTx);
            }
        }

        [TestMethod]
        public void TestUnconfTxPrevOutputSpent()
        {
            // create tx spending a previous output that exists, but is spent
            var tx = Transaction.Create(
                0,
                ImmutableArray.Create(new TxInput(UInt256.One, 0, ImmutableArray<byte>.Empty, 0)),
                ImmutableArray.Create(new TxOutput(0, ImmutableArray<byte>.Empty)),
                0).Transaction;

            // create prev output tx
            var unspentTx = new UnspentTx(tx.Inputs[0].PrevTxHash, 0, 1, 0, false, new OutputStates(1, OutputState.Spent), ImmutableArray<TxOutput>.Empty);

            // mock chain state with prev output
            var chainState = new Mock<IChainState>();
            chainState.Setup(x => x.TryGetUnspentTx(tx.Inputs[0].PrevTxHash, out unspentTx)).Returns(true);

            // mock core daemon for chain state retrieval
            var coreDaemon = new Mock<ICoreDaemon>();
            coreDaemon.Setup(x => x.GetChainState()).Returns(chainState.Object);

            using (var unconfirmedTxesBuilder = new UnconfirmedTxesBuilder(coreDaemon.Object, storageManager))
            {
                // try to add the tx
                Assert.IsFalse(unconfirmedTxesBuilder.TryAddTransaction(tx));

                // verify unconfirmed tx was added
                UnconfirmedTx unconfirmedTx;
                Assert.IsFalse(unconfirmedTxesBuilder.TryGetTransaction(tx.Hash, out unconfirmedTx));
                Assert.IsNull(unconfirmedTx);
            }
        }
    }
}
