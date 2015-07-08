﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Core;
using BitSharp.Core.Builders;
using BitSharp.Core.Domain;
using BitSharp.Core.Rules;
using BitSharp.Core.Storage;
using BitSharp.Core.Storage.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace BitSharp.Examples
{
    // methods are marked as tests to facilitate easy running of individual examples
    [TestClass]
    public class ExamplePrograms
    {
        public static void Main(string[] args)
        {
            new ExamplePrograms().RunAllExamples();

            if (Debugger.IsAttached)
            {
                Console.Write("Press any key to continue . . . ");
                Console.ReadKey();
            }
        }

        private readonly Logger logger;

        public ExamplePrograms()
        {
            var loggerNamePattern = "BitSharp.Examples.*";
            var logLevel = LogLevel.Info;

            // log layout format
            var layout = "${message} ${exception:separator=\r\n:format=message,type,method,stackTrace:maxInnerExceptionLevel=10:innerExceptionSeparator=\r\n:innerFormat=message,type,method,stackTrace}";

            // initialize logging configuration
            var config = new LoggingConfiguration();

            // create console target
            var consoleTarget = new ColoredConsoleTarget();
            consoleTarget.Layout = layout;
            config.AddTarget("console", consoleTarget);
            config.LoggingRules.Add(new LoggingRule(loggerNamePattern, logLevel, consoleTarget));

            // create debugger target, if attached
            if (Debugger.IsAttached)
            {
                var debuggerTarget = new DebuggerTarget();
                debuggerTarget.Layout = layout;
                config.AddTarget("debugger", debuggerTarget);
                config.LoggingRules.Add(new LoggingRule(loggerNamePattern, logLevel, debuggerTarget));
            }

            LogManager.Configuration = config;
            logger = LogManager.GetCurrentClassLogger();
        }

        private void RunAllExamples()
        {
            foreach (var exampleMethod in GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                logger.Info(string.Format("Running example: {0}", exampleMethod.Name));
                exampleMethod.Invoke(this, new object[0]);
                logger.Info(string.Format("Finished running example: {0}", exampleMethod.Name));
                logger.Info("-------------");
                logger.Info("");
            }

            logger.Info("Finished running examples");
            logger.Info("");
        }

        [TestMethod]
        public void ExampleDaemon()
        {
            // create example core daemon
            BlockProvider embeddedBlocks; IStorageManager storageManager;
            using (var coreDaemon = CreateExampleDaemon(out embeddedBlocks, out storageManager, maxHeight: 99))
            using (embeddedBlocks)
            using (storageManager)
            {
                // report core daemon's progress
                logger.Info(string.Format("Core daemon height: {0:N0}", coreDaemon.CurrentChain.Height));
            }
        }

        private CoreDaemon CreateExampleDaemon(out BlockProvider embeddedBlocks, out IStorageManager storageManager, int? maxHeight = null)
        {
            // retrieve first 10,000 testnet3 blocks
            embeddedBlocks = new BlockProvider("BitSharp.Examples.Blocks.TestNet3.zip");

            // initialize in-memory storage
            storageManager = new MemoryStorageManager();

            // intialize testnet3 rules (ignore script errors, script engine is not and is not intended to be complete)
            var rules = new Testnet3Rules { IgnoreScriptErrors = true };

            // initialize & start core daemon
            var coreDaemon = new CoreDaemon(rules, storageManager) { MaxHeight = maxHeight, IsStarted = true };

            // add embedded blocks
            coreDaemon.CoreStorage.AddBlocks(embeddedBlocks.ReadBlocks());

            // wait for core daemon to finish processing any available data
            coreDaemon.WaitForUpdate();

            return coreDaemon;
        }

        [TestMethod]
        public void ChainStateExample()
        {
            // create example core daemon
            BlockProvider embeddedBlocks; IStorageManager storageManager;
            using (var coreDaemon = CreateExampleDaemon(out embeddedBlocks, out storageManager, maxHeight: 999))
            using (embeddedBlocks)
            using (storageManager)
            // retrieve an immutable snapshot of the current chainstate, validation won't be blocked by an open snapshot
            using (var chainState = coreDaemon.GetChainState())
            {
                // retrieve unspent transactions
                var unspentTxes = chainState.ReadUnspentTransactions().ToList();

                // report counts
                logger.Info(string.Format("Chain.Height:                      {0,9:N0}", chainState.Chain.Height));
                logger.Info(string.Format("ReadUnspentTransactions().Count(): {0,9:N0}", unspentTxes.Count));
                logger.Info(string.Format("UnspentTxCount:                    {0,9:N0}", chainState.UnspentTxCount));
                logger.Info(string.Format("UnspentOutputCount:                {0,9:N0}", chainState.UnspentOutputCount));
                logger.Info(string.Format("TotalTxCount:                      {0,9:N0}", chainState.TotalTxCount));
                logger.Info(string.Format("TotalInputCount:                   {0,9:N0}", chainState.TotalInputCount));
                logger.Info(string.Format("TotalOutputCount:                  {0,9:N0}", chainState.TotalOutputCount));

                // look up genesis coinbase output (will be missing)
                UnspentTx unspentTx;
                chainState.TryGetUnspentTx(embeddedBlocks.GetBlock(0).Transactions[0].Hash, out unspentTx);
                logger.Info(string.Format("Genesis coinbase UnspentTx present? {0,9}", unspentTx != null));

                // look up block 1 coinbase output
                chainState.TryGetUnspentTx(embeddedBlocks.GetBlock(1).Transactions[0].Hash, out unspentTx);
                logger.Info(string.Format("Block 1 coinbase UnspenTx present? {0,9}", unspentTx != null));
                logger.Info(string.Format("Block 1 coinbase output states:    [{0}]", string.Join(",", unspentTx.OutputStates.Select(x => x.ToString()))));

                // look up block 381 list of spent txes
                IImmutableList<UInt256> spentTxes;
                chainState.TryGetBlockSpentTxes(381, out spentTxes);
                logger.Info(string.Format("Block 381 spent txes count:        {0,9:N0}", spentTxes.Count));
            }
        }

        [TestMethod]
        public void ReplayBlockExample()
        {
            // create example core daemon
            BlockProvider embeddedBlocks; IStorageManager storageManager;
            using (var coreDaemon = CreateExampleDaemon(out embeddedBlocks, out storageManager, maxHeight: 999))
            using (embeddedBlocks)
            using (storageManager)
            {
                // start a chain at the genesis block to represent the processed progress
                var processedChain = Chain.CreateForGenesisBlock(coreDaemon.Rules.GenesisChainedHeader).ToBuilder();

                // a dictionary of public key script hashes can be created for any addresses of interest, allowing for quick checking
                var scriptHashesOfInterest = new HashSet<UInt256>();

                // retrieve a chainstate to replay blocks with
                using (var chainState = coreDaemon.GetChainState())
                {
                    // enumerate the steps needed to take the currently processed chain towards the current chainstate
                    foreach (var pathElement in processedChain.NavigateTowards(chainState.Chain))
                    {
                        // retrieve the next block to replay and whether to replay forwards, or backwards for a re-org
                        var replayForward = pathElement.Item1 > 0;
                        var replayBlock = pathElement.Item2;

                        // begin replaying the transactions in the replay block
                        // if this is a re-org, the transactions will be replayed in reverse block order
                        var replayTxes = BlockReplayer.ReplayBlock(coreDaemon.CoreStorage, chainState, replayBlock.Hash, replayForward);
                        foreach (var loadedTx in replayTxes.ToEnumerable())
                        {
                            // the transaction being replayed
                            var tx = loadedTx.Transaction;

                            // the previous transactions for each of the replay transaction's inputs
                            var inputTxes = loadedTx.InputTxes;

                            // scan the replay transaction's inputs
                            if (!loadedTx.IsCoinbase)
                            {
                                for (var inputIndex = 0; inputIndex < tx.Inputs.Length; inputIndex++)
                                {
                                    var input = tx.Inputs[inputIndex];
                                    var inputPrevTx = inputTxes[inputIndex];
                                    var inputPrevTxOutput = inputPrevTx.Outputs[(int)input.PreviousTxOutputKey.TxOutputIndex];

                                    // check if the input's previous transaction output is of interest
                                    var inputPrevTxOutputPublicScriptHash = new UInt256(SHA256Static.ComputeHash(inputPrevTxOutput.ScriptPublicKey));
                                    if (scriptHashesOfInterest.Contains(inputPrevTxOutputPublicScriptHash))
                                    {
                                        if (replayForward)
                                        { /* An output for an address of interest is being spent. */ }
                                        else
                                        { /* An output for an address of interest is being "unspent", on re-org. */}
                                    }
                                }
                            }

                            // scan the replay transaction's outputs
                            for (var outputIndex = 0; outputIndex < tx.Outputs.Length; outputIndex++)
                            {
                                var output = tx.Outputs[outputIndex];

                                // check if the output is of interest
                                var outputPublicScriptHash = new UInt256(SHA256Static.ComputeHash(output.ScriptPublicKey));
                                if (scriptHashesOfInterest.Contains(outputPublicScriptHash))
                                {
                                    if (replayForward)
                                    { /* An output for an address of interest is being minted. */ }
                                    else
                                    { /* An output for an address of interest is being "unminted", on re-org. */}
                                }
                            }
                        }

                        // a wallet would now commit its progress
                        /*
                        walletDatabase.CurrentBlock = replayBlock.Hash;
                        walletDatabase.Commit();
                        */

                        // TODO: after successfully committing, a wallet would notify CoreDaemon of its current progress
                        // TODO: CoreDaemon will use this information in order to determine how far in the current chainstate it is safe to prune
                        // TODO: with this in place, if a wallet suffers a failure to commit it can just replay the block
                        // TODO: wallets can also remain disconnected from CoreDaemon, and just replay blocks to catch up when they are reconnected

                        // update the processed chain so that the next step towards the current chainstate can be taken
                        if (replayForward)
                            processedChain.AddBlock(replayBlock);
                        else
                            processedChain.RemoveBlock(replayBlock);
                    }
                }

                logger.Info("Processed chain height: {0:N0}", processedChain.Height);
            }
        }
    }
}