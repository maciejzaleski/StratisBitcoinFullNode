using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace Stratis.Bitcoin.Features.ChainDiagnostics.Models
{
    public class StakeBlockModel
    {
        [JsonProperty(PropertyName = "block")]
        public BlockTransactionDetailsModel Block { get; set; }
    }
}
