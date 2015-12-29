﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Core.Domain;
using BitSharp.Core.Script;
using NLog;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace BitSharp.Core.Rules
{
    public partial class MainnetParams : IChainParams
    {
        public MainnetParams()
        {
            GenesisChainedHeader = ChainedHeader.CreateForGenesisBlock(genesisBlock.Header);
        }

        public UInt256 GenesisHash { get; } = UInt256.ParseHex("000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f");

        public Block GenesisBlock => genesisBlock;

        public ChainedHeader GenesisChainedHeader { get; }

        public UInt256 HighestTarget { get; } = UInt256.ParseHex("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

        public int DifficultyInterval { get; } = 2016;

        public long DifficultyTargetTimespan { get; } = 14 * 24 * 60 * 60;
    }
}