using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Features.ChainDiagnostics.Controllers;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.RPC.Controllers;

namespace Stratis.Bitcoin.Features.ChainDiagnostics
{
    public class ChainDiagnosticsFeature : FullNodeFeature
    {
        private readonly FullNode fullNode;

        private readonly NodeSettings nodeSettings;

        private readonly ILogger logger;

        private readonly IFullNodeBuilder fullNodeBuilder;

        public ChainDiagnosticsFeature(IFullNodeBuilder fullNodeBuilder, FullNode fullNode, NodeSettings nodeSettings, ILoggerFactory loggerFactory)
        {
            this.fullNodeBuilder = fullNodeBuilder;
            this.fullNode = fullNode;
            this.nodeSettings = nodeSettings;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        public override Task InitializeAsync()
        {           
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderChainDiagnosticsExtension
    {
        public static IFullNodeBuilder UseChainDiagnostics(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ChainDiagnosticsFeature>("ChainDiagnostics");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ChainDiagnosticsFeature>()
                    .FeatureServices(services => services.AddSingleton(fullNodeBuilder));
            });

            fullNodeBuilder.ConfigureServices(service =>
            {
                service.AddSingleton<ChainDiagnosticsController>();
            });

            return fullNodeBuilder;
        }
    }
}
