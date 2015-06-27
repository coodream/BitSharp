﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Core;
using BitSharp.Core.Domain;
using BitSharp.Core.ExtensionMethods;
using BitSharp.Core.Storage;
using Microsoft.Isam.Esent.Interop;
using Microsoft.Isam.Esent.Interop.Server2003;
using Microsoft.Isam.Esent.Interop.Windows8;
using Microsoft.Isam.Esent.Interop.Windows81;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using BitSharp.Esent.ChainState;

namespace BitSharp.Esent
{
    internal class ChainStateCursor : IChainStateCursor
    {
        //TODO
        public static bool IndexOutputs { get; set; }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string jetDatabase;
        private readonly Instance jetInstance;

        public readonly Session jetSession;
        public readonly JET_DBID chainStateDbId;

        public readonly JET_TABLEID globalsTableId;
        public readonly JET_COLUMNID unspentTxCountColumnId;
        public readonly JET_COLUMNID unspentOutputCountColumnId;
        public readonly JET_COLUMNID totalTxCountColumnId;
        public readonly JET_COLUMNID totalInputCountColumnId;
        public readonly JET_COLUMNID totalOutputCountColumnId;

        public readonly JET_TABLEID flushTableId;
        public readonly JET_COLUMNID flushColumnId;

        public readonly JET_TABLEID chainTableId;
        public readonly JET_COLUMNID blockHeightColumnId;
        public readonly JET_COLUMNID chainedHeaderBytesColumnId;

        public readonly JET_TABLEID unspentTxTableId;
        public readonly JET_COLUMNID txHashColumnId;
        public readonly JET_COLUMNID blockIndexColumnId;
        public readonly JET_COLUMNID txIndexColumnId;
        public readonly JET_COLUMNID outputStatesColumnId;

        public readonly JET_TABLEID spentTxTableId;
        public readonly JET_COLUMNID spentSpentBlockIndexColumnId;
        public readonly JET_COLUMNID spentDataColumnId;

        public readonly JET_TABLEID unmintedTxTableId;
        public readonly JET_COLUMNID unmintedBlockHashColumnId;
        public readonly JET_COLUMNID unmintedDataColumnId;

        private bool inTransaction;

        public ChainStateCursor(string jetDatabase, Instance jetInstance)
        {
            this.jetDatabase = jetDatabase;
            this.jetInstance = jetInstance;

            //TODO
            var readOnly = false;

            this.OpenCursor(this.jetDatabase, this.jetInstance, readOnly,
                out this.jetSession,
                out this.chainStateDbId,
                out this.globalsTableId,
                    out this.unspentTxCountColumnId,
                    out this.unspentOutputCountColumnId,
                    out this.totalTxCountColumnId,
                    out this.totalInputCountColumnId,
                    out this.totalOutputCountColumnId,
                out this.flushTableId,
                    out this.flushColumnId,
                out this.chainTableId,
                    out this.blockHeightColumnId,
                    out this.chainedHeaderBytesColumnId,
                out this.unspentTxTableId,
                    out this.txHashColumnId,
                    out this.blockIndexColumnId,
                    out this.txIndexColumnId,
                    out this.outputStatesColumnId,
                out spentTxTableId,
                    out spentSpentBlockIndexColumnId,
                    out spentDataColumnId,
                out unmintedTxTableId,
                    out unmintedBlockHashColumnId,
                    out unmintedDataColumnId);
        }

        ~ChainStateCursor()
        {
            this.Dispose();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Api.JetCloseDatabase(this.jetSession, this.chainStateDbId, CloseDatabaseGrbit.None);
            this.jetSession.Dispose();
        }

        public bool InTransaction
        {
            get { return this.inTransaction; }
        }

        public IEnumerable<ChainedHeader> ReadChain()
        {
            Api.JetSetCurrentIndex(this.jetSession, this.chainTableId, "IX_BlockHeight");

            if (Api.TryMoveFirst(this.jetSession, this.chainTableId))
            {
                do
                {
                    var chainedHeader = DataEncoder.DecodeChainedHeader(Api.RetrieveColumn(this.jetSession, this.chainTableId, this.chainedHeaderBytesColumnId));
                    yield return chainedHeader;
                }
                while (Api.TryMoveNext(this.jetSession, this.chainTableId));
            }
        }

        public ChainedHeader GetChainTip()
        {
            Api.JetSetCurrentIndex(this.jetSession, this.chainTableId, "IX_BlockHeight");

            if (Api.TryMoveLast(this.jetSession, this.chainTableId))
            {
                var chainedHeader = DataEncoder.DecodeChainedHeader(Api.RetrieveColumn(this.jetSession, this.chainTableId, this.chainedHeaderBytesColumnId));
                return chainedHeader;
            }
            else
            {
                return null;
            }
        }

        public void AddChainedHeader(ChainedHeader chainedHeader)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            try
            {
                using (var jetUpdate = this.jetSession.BeginUpdate(this.chainTableId, JET_prep.Insert))
                {
                    Api.SetColumns(this.jetSession, this.chainTableId,
                        new Int32ColumnValue { Columnid = this.blockHeightColumnId, Value = chainedHeader.Height },
                        new BytesColumnValue { Columnid = this.chainedHeaderBytesColumnId, Value = DataEncoder.EncodeChainedHeader(chainedHeader) });

                    jetUpdate.Save();
                }
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Failed to add chained header.", e);
            }
        }

        public void RemoveChainedHeader(ChainedHeader chainedHeader)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetSetCurrentIndex(this.jetSession, this.chainTableId, "IX_BlockHeight");

            Api.MakeKey(this.jetSession, this.chainTableId, chainedHeader.Height, MakeKeyGrbit.NewKey);

            if (!Api.TrySeek(this.jetSession, this.chainTableId, SeekGrbit.SeekEQ))
                throw new InvalidOperationException();

            Api.JetDelete(this.jetSession, this.chainTableId);
        }

        public int UnspentTxCount
        {
            get
            {
                return Api.RetrieveColumnAsInt32(this.jetSession, this.globalsTableId, this.unspentTxCountColumnId).Value;
            }
            set
            {
                if (!this.inTransaction)
                    throw new InvalidOperationException();

                using (var jetUpdate = this.jetSession.BeginUpdate(this.globalsTableId, JET_prep.Replace))
                {
                    Api.SetColumn(this.jetSession, this.globalsTableId, this.unspentTxCountColumnId, value);
                    jetUpdate.Save();
                }
            }
        }

        public int UnspentOutputCount
        {
            get
            {
                return Api.RetrieveColumnAsInt32(this.jetSession, this.globalsTableId, this.unspentOutputCountColumnId).Value;
            }
            set
            {
                if (!this.inTransaction)
                    throw new InvalidOperationException();

                using (var jetUpdate = this.jetSession.BeginUpdate(this.globalsTableId, JET_prep.Replace))
                {
                    Api.SetColumn(this.jetSession, this.globalsTableId, this.unspentOutputCountColumnId, value);
                    jetUpdate.Save();
                }
            }
        }

        public int TotalTxCount
        {
            get
            {
                return Api.RetrieveColumnAsInt32(this.jetSession, this.globalsTableId, this.totalTxCountColumnId).Value;
            }
            set
            {
                if (!this.inTransaction)
                    throw new InvalidOperationException();

                using (var jetUpdate = this.jetSession.BeginUpdate(this.globalsTableId, JET_prep.Replace))
                {
                    Api.SetColumn(this.jetSession, this.globalsTableId, this.totalTxCountColumnId, value);
                    jetUpdate.Save();
                }
            }
        }

        public int TotalInputCount
        {
            get
            {
                return Api.RetrieveColumnAsInt32(this.jetSession, this.globalsTableId, this.totalInputCountColumnId).Value;
            }
            set
            {
                if (!this.inTransaction)
                    throw new InvalidOperationException();

                using (var jetUpdate = this.jetSession.BeginUpdate(this.globalsTableId, JET_prep.Replace))
                {
                    Api.SetColumn(this.jetSession, this.globalsTableId, this.totalInputCountColumnId, value);
                    jetUpdate.Save();
                }
            }
        }

        public int TotalOutputCount
        {
            get
            {
                return Api.RetrieveColumnAsInt32(this.jetSession, this.globalsTableId, this.totalOutputCountColumnId).Value;
            }
            set
            {
                if (!this.inTransaction)
                    throw new InvalidOperationException();

                using (var jetUpdate = this.jetSession.BeginUpdate(this.globalsTableId, JET_prep.Replace))
                {
                    Api.SetColumn(this.jetSession, this.globalsTableId, this.totalOutputCountColumnId, value);
                    jetUpdate.Save();
                }
            }
        }

        public bool ContainsUnspentTx(UInt256 txHash)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.unspentTxTableId, "IX_TxHash");
            Api.MakeKey(this.jetSession, this.unspentTxTableId, DbEncoder.EncodeUInt256(txHash), MakeKeyGrbit.NewKey);
            return Api.TrySeek(this.jetSession, this.unspentTxTableId, SeekGrbit.SeekEQ);
        }

        public bool TryGetUnspentTx(UInt256 txHash, out UnspentTx unspentTx)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.unspentTxTableId, "IX_TxHash");
            Api.MakeKey(this.jetSession, this.unspentTxTableId, DbEncoder.EncodeUInt256(txHash), MakeKeyGrbit.NewKey);
            if (Api.TrySeek(this.jetSession, this.unspentTxTableId, SeekGrbit.SeekEQ))
            {
                var blockIndexColumn = new Int32ColumnValue { Columnid = this.blockIndexColumnId };
                var txIndexColumn = new Int32ColumnValue { Columnid = this.txIndexColumnId };
                var outputStatesColumn = new BytesColumnValue { Columnid = this.outputStatesColumnId };
                Api.RetrieveColumns(this.jetSession, this.unspentTxTableId, blockIndexColumn, txIndexColumn, outputStatesColumn);

                var blockIndex = blockIndexColumn.Value.Value;
                var txIndex = txIndexColumn.Value.Value;
                var outputStates = DataEncoder.DecodeOutputStates(outputStatesColumn.Value);

                unspentTx = new UnspentTx(txHash, blockIndex, txIndex, outputStates);
                return true;
            }

            unspentTx = default(UnspentTx);
            return false;
        }

        public bool TryAddUnspentTx(UnspentTx unspentTx)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            try
            {
                using (var jetUpdate = this.jetSession.BeginUpdate(this.unspentTxTableId, JET_prep.Insert))
                {
                    Api.SetColumns(this.jetSession, this.unspentTxTableId,
                        new BytesColumnValue { Columnid = this.txHashColumnId, Value = DbEncoder.EncodeUInt256(unspentTx.TxHash) },
                        new Int32ColumnValue { Columnid = this.blockIndexColumnId, Value = unspentTx.BlockIndex },
                        new Int32ColumnValue { Columnid = this.txIndexColumnId, Value = unspentTx.TxIndex },
                        new BytesColumnValue { Columnid = this.outputStatesColumnId, Value = DataEncoder.EncodeOutputStates(unspentTx.OutputStates) });

                    jetUpdate.Save();
                }

                return true;
            }
            catch (EsentKeyDuplicateException)
            {
                return false;
            }
        }

        public bool TryRemoveUnspentTx(UInt256 txHash)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetSetCurrentIndex(this.jetSession, this.unspentTxTableId, "IX_TxHash");
            Api.MakeKey(this.jetSession, this.unspentTxTableId, DbEncoder.EncodeUInt256(txHash), MakeKeyGrbit.NewKey);
            if (Api.TrySeek(this.jetSession, this.unspentTxTableId, SeekGrbit.SeekEQ))
            {
                Api.JetDelete(this.jetSession, this.unspentTxTableId);

                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TryUpdateUnspentTx(UnspentTx unspentTx)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetSetCurrentIndex(this.jetSession, this.unspentTxTableId, "IX_TxHash");
            Api.MakeKey(this.jetSession, this.unspentTxTableId, DbEncoder.EncodeUInt256(unspentTx.TxHash), MakeKeyGrbit.NewKey);

            if (Api.TrySeek(this.jetSession, this.unspentTxTableId, SeekGrbit.SeekEQ))
            {
                using (var jetUpdate = this.jetSession.BeginUpdate(this.unspentTxTableId, JET_prep.Replace))
                {
                    Api.SetColumn(this.jetSession, this.unspentTxTableId, this.outputStatesColumnId, DataEncoder.EncodeOutputStates(unspentTx.OutputStates));

                    jetUpdate.Save();
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        public IEnumerable<UnspentTx> ReadUnspentTransactions()
        {
            Api.JetSetCurrentIndex(this.jetSession, this.unspentTxTableId, "IX_TxHash");

            if (Api.TryMoveFirst(this.jetSession, this.unspentTxTableId))
            {
                do
                {
                    var txHashColumn = new BytesColumnValue { Columnid = this.txHashColumnId };
                    var blockIndexColumn = new Int32ColumnValue { Columnid = this.blockIndexColumnId };
                    var txIndexColumn = new Int32ColumnValue { Columnid = this.txIndexColumnId };
                    var outputStatesColumn = new BytesColumnValue { Columnid = this.outputStatesColumnId };
                    Api.RetrieveColumns(this.jetSession, this.unspentTxTableId, txHashColumn, blockIndexColumn, txIndexColumn, outputStatesColumn);

                    var txHash = DbEncoder.DecodeUInt256(txHashColumn.Value);
                    var blockIndex = blockIndexColumn.Value.Value;
                    var txIndex = txIndexColumn.Value.Value;
                    var outputStates = DataEncoder.DecodeOutputStates(outputStatesColumn.Value);

                    yield return new UnspentTx(txHash, blockIndex, txIndex, outputStates);
                }
                while (Api.TryMoveNext(this.jetSession, this.unspentTxTableId));
            }
        }

        public bool ContainsBlockSpentTxes(int blockIndex)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.spentTxTableId, "IX_SpentBlockIndex");
            Api.MakeKey(this.jetSession, this.spentTxTableId, blockIndex, MakeKeyGrbit.NewKey);
            return Api.TrySeek(this.jetSession, this.spentTxTableId, SeekGrbit.SeekEQ);
        }

        public bool TryGetBlockSpentTxes(int blockIndex, out IImmutableList<UInt256> spentTxes)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.spentTxTableId, "IX_SpentBlockIndex");

            Api.MakeKey(this.jetSession, this.spentTxTableId, blockIndex, MakeKeyGrbit.NewKey);

            if (Api.TrySeek(this.jetSession, this.spentTxTableId, SeekGrbit.SeekEQ))
            {
                var spentTxesBytes = Api.RetrieveColumn(this.jetSession, this.spentTxTableId, this.spentDataColumnId);

                using (var stream = new MemoryStream(spentTxesBytes))
                using (var reader = new BinaryReader(stream))
                {
                    spentTxes = ImmutableList.CreateRange(reader.ReadList(() => DataEncoder.DecodeUInt256(reader)));
                }

                return true;
            }
            else
            {
                spentTxes = null;
                return false;
            }
        }

        public bool TryAddBlockSpentTxes(int blockIndex, IImmutableList<UInt256> spentTxes)
        {
            try
            {
                using (var jetUpdate = this.jetSession.BeginUpdate(this.spentTxTableId, JET_prep.Insert))
                {
                    byte[] spentTxesBytes;
                    using (var stream = new MemoryStream())
                    using (var writer = new BinaryWriter(stream))
                    {
                        writer.WriteList(spentTxes.ToImmutableArray(), spentTx => DataEncoder.EncodeUInt256(writer, spentTx));
                        spentTxesBytes = stream.ToArray();
                    }

                    Api.SetColumns(this.jetSession, this.spentTxTableId,
                        new Int32ColumnValue { Columnid = this.spentSpentBlockIndexColumnId, Value = blockIndex },
                        new BytesColumnValue { Columnid = this.spentDataColumnId, Value = spentTxesBytes });

                    jetUpdate.Save();
                }

                return true;
            }
            catch (EsentKeyDuplicateException)
            {
                return false;
            }
        }

        public bool TryRemoveBlockSpentTxes(int blockIndex)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.spentTxTableId, "IX_SpentBlockIndex");

            Api.MakeKey(this.jetSession, this.spentTxTableId, blockIndex, MakeKeyGrbit.NewKey);

            if (Api.TrySeek(this.jetSession, this.spentTxTableId, SeekGrbit.SeekEQ))
            {
                Api.JetDelete(this.jetSession, this.spentTxTableId);
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool ContainsBlockUnmintedTxes(UInt256 blockHash)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.unmintedTxTableId, "IX_UnmintedBlockHash");
            Api.MakeKey(this.jetSession, this.unmintedTxTableId, DbEncoder.EncodeUInt256(blockHash), MakeKeyGrbit.NewKey);
            return Api.TrySeek(this.jetSession, this.unmintedTxTableId, SeekGrbit.SeekEQ);
        }

        public bool TryGetBlockUnmintedTxes(UInt256 blockHash, out IImmutableList<UnmintedTx> unmintedTxes)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.unmintedTxTableId, "IX_UnmintedBlockHash");

            Api.MakeKey(this.jetSession, this.unmintedTxTableId, DbEncoder.EncodeUInt256(blockHash), MakeKeyGrbit.NewKey);

            if (Api.TrySeek(this.jetSession, this.unmintedTxTableId, SeekGrbit.SeekEQ))
            {
                var unmintedTxesBytes = Api.RetrieveColumn(this.jetSession, this.unmintedTxTableId, this.unmintedDataColumnId);

                using (var stream = new MemoryStream(unmintedTxesBytes))
                using (var reader = new BinaryReader(stream))
                {
                    unmintedTxes = ImmutableList.CreateRange(reader.ReadList(() => DataEncoder.DecodeUnmintedTx(reader)));
                }

                return true;
            }
            else
            {
                unmintedTxes = null;
                return false;
            }
        }

        public bool TryAddBlockUnmintedTxes(UInt256 blockHash, IImmutableList<UnmintedTx> unmintedTxes)
        {
            try
            {
                using (var jetUpdate = this.jetSession.BeginUpdate(this.unmintedTxTableId, JET_prep.Insert))
                {
                    byte[] unmintedTxesBytes;
                    using (var stream = new MemoryStream())
                    using (var writer = new BinaryWriter(stream))
                    {
                        writer.WriteList(unmintedTxes.ToImmutableArray(), unmintedTx => DataEncoder.EncodeUnmintedTx(writer, unmintedTx));
                        unmintedTxesBytes = stream.ToArray();
                    }

                    Api.SetColumns(this.jetSession, this.unmintedTxTableId,
                        new BytesColumnValue { Columnid = this.unmintedBlockHashColumnId, Value = DbEncoder.EncodeUInt256(blockHash) },
                        new BytesColumnValue { Columnid = this.unmintedDataColumnId, Value = unmintedTxesBytes });

                    jetUpdate.Save();
                }

                return true;
            }
            catch (EsentKeyDuplicateException)
            {
                return false;
            }
        }

        public bool TryRemoveBlockUnmintedTxes(UInt256 blockHash)
        {
            Api.JetSetCurrentIndex(this.jetSession, this.unmintedTxTableId, "IX_UnmintedBlockHash");

            Api.MakeKey(this.jetSession, this.unmintedTxTableId, DbEncoder.EncodeUInt256(blockHash), MakeKeyGrbit.NewKey);

            if (Api.TrySeek(this.jetSession, this.unmintedTxTableId, SeekGrbit.SeekEQ))
            {
                Api.JetDelete(this.jetSession, this.unmintedTxTableId);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void BeginTransaction(bool readOnly)
        {
            if (this.inTransaction)
                throw new InvalidOperationException();

            Api.JetBeginTransaction2(this.jetSession, readOnly ? BeginTransactionGrbit.ReadOnly : BeginTransactionGrbit.None);

            this.inTransaction = true;
        }

        public void CommitTransaction()
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetCommitTransaction(this.jetSession, CommitTransactionGrbit.LazyFlush);

            this.inTransaction = false;
        }

        public void RollbackTransaction()
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetRollback(this.jetSession, RollbackTransactionGrbit.None);

            this.inTransaction = false;
        }

        public void Flush()
        {
            if (EsentVersion.SupportsServer2003Features)
            {
                Api.JetCommitTransaction(this.jetSession, Server2003Grbits.WaitAllLevel0Commit);
            }
            else
            {
                using (var jetTx = this.jetSession.BeginTransaction())
                {
                    Api.EscrowUpdate(this.jetSession, this.flushTableId, this.flushColumnId, 1);
                    jetTx.Commit(CommitTransactionGrbit.None);
                }
            }
        }

        public void Defragment()
        {
            //int passes = -1, seconds = -1;
            //Api.JetDefragment(defragCursor.jetSession, defragCursor.chainStateDbId, "Chain", ref passes, ref seconds, DefragGrbit.BatchStart);
            //Api.JetDefragment(defragCursor.jetSession, defragCursor.chainStateDbId, "ChainState", ref passes, ref seconds, DefragGrbit.BatchStart);

            if (EsentVersion.SupportsWindows81Features)
            {
                logger.Info("Begin shrinking chain state database");

                int actualPages;
                Windows8Api.JetResizeDatabase(this.jetSession, this.chainStateDbId, 0, out actualPages, Windows81Grbits.OnlyShrink);

                logger.Info("Finished shrinking chain state database: {0:#,##0} MB".Format2((float)actualPages * SystemParameters.DatabasePageSize / 1.MILLION()));
            }
        }

        private void OpenCursor(string jetDatabase, Instance jetInstance, bool readOnly,
            out Session jetSession,
            out JET_DBID chainStateDbId,
            out JET_TABLEID globalsTableId,
            out JET_COLUMNID unspentTxCountColumnId,
            out JET_COLUMNID unspentOutputCountColumnId,
            out JET_COLUMNID totalTxCountColumnId,
            out JET_COLUMNID totalInputCountColumnId,
            out JET_COLUMNID totalOutputCountColumnId,
            out JET_TABLEID flushTableId,
            out JET_COLUMNID flushColumnId,
            out JET_TABLEID chainTableId,
            out JET_COLUMNID blockHeightColumnId,
            out JET_COLUMNID chainedHeaderBytesColumnId,
            out JET_TABLEID unspentTxTableId,
            out JET_COLUMNID txHashColumnId,
            out JET_COLUMNID blockIndexColumnId,
            out JET_COLUMNID txIndexColumnId,
            out JET_COLUMNID outputStatesColumnId,
            out JET_TABLEID spentTxTableId,
            out JET_COLUMNID spentSpentBlockIndexColumnId,
            out JET_COLUMNID spentDataColumnId,
            out JET_TABLEID unmintedTxTableId,
            out JET_COLUMNID unmintedBlockHashColumnId,
            out JET_COLUMNID unmintedDataColumnId)
        {
            var success = false;
            jetSession = new Session(jetInstance);
            try
            {
                Api.JetOpenDatabase(jetSession, jetDatabase, "", out chainStateDbId, readOnly ? OpenDatabaseGrbit.ReadOnly : OpenDatabaseGrbit.None);

                Api.JetOpenTable(jetSession, chainStateDbId, "Globals", null, 0, readOnly ? OpenTableGrbit.ReadOnly : OpenTableGrbit.None, out globalsTableId);
                unspentTxCountColumnId = Api.GetTableColumnid(jetSession, globalsTableId, "UnspentTxCount");
                unspentOutputCountColumnId = Api.GetTableColumnid(jetSession, globalsTableId, "UnspentOutputCount");
                totalTxCountColumnId = Api.GetTableColumnid(jetSession, globalsTableId, "TotalTxCount");
                totalInputCountColumnId = Api.GetTableColumnid(jetSession, globalsTableId, "TotalInputCount");
                totalOutputCountColumnId = Api.GetTableColumnid(jetSession, globalsTableId, "TotalOutputCount");

                if (!Api.TryMoveFirst(jetSession, globalsTableId))
                    throw new InvalidOperationException();

                Api.JetOpenTable(jetSession, chainStateDbId, "Flush", null, 0, readOnly ? OpenTableGrbit.ReadOnly : OpenTableGrbit.None, out flushTableId);
                flushColumnId = Api.GetTableColumnid(jetSession, flushTableId, "Flush");

                if (!Api.TryMoveFirst(jetSession, flushTableId))
                    throw new InvalidOperationException();

                Api.JetOpenTable(jetSession, chainStateDbId, "Chain", null, 0, readOnly ? OpenTableGrbit.ReadOnly : OpenTableGrbit.None, out chainTableId);
                blockHeightColumnId = Api.GetTableColumnid(jetSession, chainTableId, "BlockHeight");
                chainedHeaderBytesColumnId = Api.GetTableColumnid(jetSession, chainTableId, "ChainedHeaderBytes");

                Api.JetOpenTable(jetSession, chainStateDbId, "UnspentTx", null, 0, readOnly ? OpenTableGrbit.ReadOnly : OpenTableGrbit.None, out unspentTxTableId);
                txHashColumnId = Api.GetTableColumnid(jetSession, unspentTxTableId, "TxHash");
                blockIndexColumnId = Api.GetTableColumnid(jetSession, unspentTxTableId, "BlockIndex");
                txIndexColumnId = Api.GetTableColumnid(jetSession, unspentTxTableId, "TxIndex");
                outputStatesColumnId = Api.GetTableColumnid(jetSession, unspentTxTableId, "OutputStates");

                Api.JetOpenTable(jetSession, chainStateDbId, "SpentTx", null, 0, readOnly ? OpenTableGrbit.ReadOnly : OpenTableGrbit.None, out spentTxTableId);
                spentSpentBlockIndexColumnId = Api.GetTableColumnid(jetSession, spentTxTableId, "SpentBlockIndex");
                spentDataColumnId = Api.GetTableColumnid(jetSession, spentTxTableId, "SpentData");

                Api.JetOpenTable(jetSession, chainStateDbId, "UnmintedTx", null, 0, readOnly ? OpenTableGrbit.ReadOnly : OpenTableGrbit.None, out unmintedTxTableId);
                unmintedBlockHashColumnId = Api.GetTableColumnid(jetSession, unmintedTxTableId, "BlockHash");
                unmintedDataColumnId = Api.GetTableColumnid(jetSession, unmintedTxTableId, "UnmintedData");

                success = true;
            }
            finally
            {
                if (!success)
                    jetSession.Dispose();
            }
        }
    }
}
