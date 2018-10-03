using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
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
        private readonly IConsensusManager consensusManager;

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
            this.consensusManager = consensusManager;
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
        public async Task<IActionResult> StakeBlock([FromBody]StakeBlockRequest request)
        {
            List<object> overallResult = new List<object>();

            for (int iBlockIndex = 0 ; iBlockIndex < request.BlockCount; iBlockIndex++)
            {
                ChainedHeader chainTip = this.chain.Tip;
                CancellationTokenSource stakeCancellationTokenSource =
                    CancellationTokenSource.CreateLinkedTokenSource(new[] {this.nodeLifetime.ApplicationStopping});

                uint coinstakeTimestamp = (uint) this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp() &
                                          ~PosTimeMaskRule.StakeTimestampMask;

                WalletSecret walletSecret = new WalletSecret
                {
                    WalletName = request.WalletName,
                    WalletPassword = request.WalletPassword
                };

                FieldInfo stakeCancellationTokenSourceField =
                    typeof(PosMinting).GetField("stakeCancellationTokenSource",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                stakeCancellationTokenSourceField.SetValue(this.posMinting,
                    CancellationTokenSource.CreateLinkedTokenSource(new[] {this.nodeLifetime.ApplicationStopping}));

                MethodInfo getUtxoStakeDescriptionsAsyncMethod =
                    typeof(PosMinting).GetMethod("GetUtxoStakeDescriptionsAsync",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                List<UtxoStakeDescription> utxoStakeDescriptions =
                    await (Task<List<UtxoStakeDescription>>) getUtxoStakeDescriptionsAsyncMethod.Invoke(posMinting,
                        new object[] {walletSecret, stakeCancellationTokenSource.Token});

                BlockTemplate blockTemplate = this.blockProvider.BuildPosBlock(chainTip, new Script());
                var posBlock = (PosBlock) blockTemplate.Block;

                posBlock.Header.Version = request.Version;
                posBlock.Header.Nonce = request.Nonce;
                posBlock.Header.Time += request.TimeOffset;

                var coinstakeContext = new CoinstakeContext();
                coinstakeContext.CoinstakeTx = this.nodeSettings.Network.CreateTransaction();
                coinstakeContext.CoinstakeTx.Time = coinstakeTimestamp;

                // Search to current coinstake time.
                long searchTime = coinstakeContext.CoinstakeTx.Time;

                var lastCoinStakeSearchTime = chainTip.Header.Time;
                long searchInterval = searchTime - lastCoinStakeSearchTime;

                CancellationTokenSource stakingCancelationTokenSource = new CancellationTokenSource();
                stakingCancelationTokenSource.CancelAfter(request.StakingTimeout * 1000);
                CancellationToken waitingCancelationToken = stakingCancelationTokenSource.Token;
                CancellationToken stakingCancelationToken = stakingCancelationTokenSource.Token;

                while (coinstakeTimestamp <= lastCoinStakeSearchTime)
                {
                    coinstakeTimestamp = (uint) this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp() &
                                         ~PosTimeMaskRule.StakeTimestampMask;
                    lastCoinStakeSearchTime = chainTip.Header.Time;
                    Thread.Sleep(500);
                    if (waitingCancelationToken.IsCancellationRequested) break;
                }

                if (coinstakeTimestamp <= lastCoinStakeSearchTime)
                {
                    return this.Json(
                        $"Current coinstake time {coinstakeTimestamp} is not greater than last search timestamp {lastCoinStakeSearchTime}.");
                }

                bool coinstakeCreated = false;
                while (true)
                {
                    if (stakingCancelationToken.IsCancellationRequested) break;

                    coinstakeCreated = await this.posMinting.CreateCoinstakeAsync(utxoStakeDescriptions, posBlock,
                        chainTip, searchInterval,
                        blockTemplate.TotalFee, coinstakeContext);

                    if (coinstakeCreated)
                    {
                        uint minTimestamp = chainTip.Header.Time + 1;
                        if (coinstakeContext.CoinstakeTx.Time >= minTimestamp)
                        {
                            // Make sure coinstake would meet timestamp protocol
                            // as it would be the same as the block timestamp.
                            posBlock.Transactions[0].Time = posBlock.Header.Time = coinstakeContext.CoinstakeTx.Time;

                            // We have to make sure that we have no future timestamps in
                            // our transactions set.
                            for (int i = posBlock.Transactions.Count - 1; i >= 0; i--)
                            {
                                if (posBlock.Transactions[i].Time > posBlock.Header.Time)
                                {
                                    posBlock.Transactions.Remove(posBlock.Transactions[i]);
                                }
                            }

                            posBlock.Transactions.Insert(1, coinstakeContext.CoinstakeTx);
                            posBlock.UpdateMerkleRoot();

                            // Append a signature to our block.
                            ECDSASignature signature = coinstakeContext.Key.Sign(posBlock.GetHash());

                            posBlock.BlockSignature = new BlockSignature {Signature = signature.ToDER()};
                            break;
                        }
                    }
                }

                var stakeBlockModel = new StakeBlockModel();
                stakeBlockModel.Block = new BlockTransactionDetailsModel(posBlock, this.nodeSettings.Network);

                if (coinstakeCreated && request.AddToBlockchain)
                {
                    try
                    {
                        ChainedHeader chainedHeader =
                            await this.consensusManager.BlockMinedAsync(posBlock).ConfigureAwait(false);
                    }
                    catch (ConsensusRuleException ex)
                    {
                        var consensusRuleExceptionResult = new { BlockNumber = iBlockIndex, Result = new { ex.ConsensusError.Message, ex.StackTrace } };
                        overallResult.Add(consensusRuleExceptionResult);
                    }
                    catch (Exception ex)
                    {
                        var exceptionResult = new { BlockNumber = iBlockIndex, Result = new { ex.Message, ex.StackTrace } };
                        overallResult.Add(exceptionResult);
                    }

                    var result = new { BlockNumber = iBlockIndex, Result = stakeBlockModel };
                    overallResult.Add(result);
                }
                else
                {
                    var result = new { BlockNumber = iBlockIndex, Result = stakeBlockModel };
                    overallResult.Add(result);
                }

                Thread.Sleep(5000);
            }

            return this.Json(overallResult);
        }
    }
}
