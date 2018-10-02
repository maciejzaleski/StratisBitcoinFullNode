using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.ChainDiagnostics.Models;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.Miner.Staking;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;
using Script = NBitcoin.Script;

namespace Stratis.Bitcoin.Features.ChainDiagnostics.Controllers
{
    /// <summary>
    /// Controller providing API operations on the ChainDiagnostics feature.
    /// </summary>
    [Route("api/[controller]")]
    public class ChainDiagnosticsController : Controller
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>An interface implementation used to retrieve a transaction.</summary>
        private readonly IPooledTransaction pooledTransaction;

        /// <summary>An interface implementation used to retrieve unspent transactions from a pooled source.</summary>
        private readonly IPooledGetUnspentTransaction pooledGetUnspentTransaction;

        /// <summary>An interface implementation used to retrieve unspent transactions.</summary>
        private readonly IGetUnspentTransaction getUnspentTransaction;

        /// <summary>An interface implementation used to retrieve the network difficulty target.</summary>
        private readonly INetworkDifficulty networkDifficulty;

        /// <summary>An interface implementation for the blockstore.</summary>
        private readonly IBlockStore blockStore;

        /// <summary>POS staker.</summary>
        private readonly IPosMinting posMinting;

        private readonly NodeSettings nodeSettings;

        private readonly IDateTimeProvider dateTimeProvider;

        private readonly ConcurrentChain chain;
        private readonly IBlockProvider blockProvider;
        private readonly INodeLifetime nodeLifetime;

        public ChainDiagnosticsController(
            ILoggerFactory loggerFactory,
            IPooledTransaction pooledTransaction,
            IPooledGetUnspentTransaction pooledGetUnspentTransaction,
            IGetUnspentTransaction getUnspentTransaction,
            INetworkDifficulty networkDifficulty,
            IFullNode fullNode,
            NodeSettings nodeSettings,
            Network network,
            ConcurrentChain chain,
            IChainState chainState,
            Connection.IConnectionManager connectionManager,
            IConsensusManager consensusManager,
            IBlockStore blockStore,
            IPosMinting posMinting,
            IDateTimeProvider dateTimeProvider,
            IBlockProvider blockProvider,
            INodeLifetime nodeLifetime)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.pooledTransaction = pooledTransaction;
            this.pooledGetUnspentTransaction = pooledGetUnspentTransaction;
            this.getUnspentTransaction = getUnspentTransaction;
            this.networkDifficulty = networkDifficulty;
            this.nodeSettings = nodeSettings;
            this.chain = chain;
            this.blockStore = blockStore;
            this.posMinting = posMinting;
            this.dateTimeProvider = dateTimeProvider;
            this.blockProvider = blockProvider;
            this.nodeLifetime = nodeLifetime;
        }

        /// <summary>
        /// Stops the full node.
        /// </summary>
        [Route("StakeBlock")]
        [HttpPost]
        public async Task StakeBlock([FromBody]RequestModels.StakeBlockRequest request)
        {
            ChainedHeader chainTip = this.chain.Tip;
            CancellationTokenSource stakeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(new[] { this.nodeLifetime.ApplicationStopping });

            uint coinstakeTimestamp = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp() & ~PosTimeMaskRule.StakeTimestampMask;

            WalletSecret walletSecret = new WalletSecret
            {
                WalletName = request.WalletName,
                WalletPassword = request.WalletPassword
            };

            MethodInfo getUtxoStakeDescriptionsAsyncMethod = typeof(PosMinting).GetMethod("GetUtxoStakeDescriptionsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            List<UtxoStakeDescription> utxoStakeDescriptions = await (Task<List<UtxoStakeDescription>>)getUtxoStakeDescriptionsAsyncMethod.Invoke(this, new object[] { walletSecret, stakeCancellationTokenSource });

            BlockTemplate blockTemplate = this.blockProvider.BuildPosBlock(chainTip, new Script());
            var posBlock = (PosBlock)blockTemplate.Block;

            var coinstakeContext = new CoinstakeContext();
            coinstakeContext.CoinstakeTx = this.nodeSettings.Network.CreateTransaction();
            coinstakeContext.CoinstakeTx.Time = coinstakeTimestamp;

            // Search to current coinstake time.
            long searchTime = coinstakeContext.CoinstakeTx.Time;

            var lastCoinStakeSearchTime = this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp();
            long searchInterval = searchTime - lastCoinStakeSearchTime;

            await this.posMinting.CreateCoinstakeAsync(utxoStakeDescriptions, posBlock, chainTip, searchInterval, blockTemplate.TotalFee, coinstakeContext);
        }
    }
}
