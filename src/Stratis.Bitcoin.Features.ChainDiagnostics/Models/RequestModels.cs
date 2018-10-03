using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Validations;
using Stratis.Bitcoin.Utilities.ValidationAttributes;

namespace Stratis.Bitcoin.Features.ChainDiagnostics.Models
{
    public class RequestModel
    {
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    /// <summary>
    /// Object used to create a new wallet
    /// </summary>
    public class StakeBlockRequest : RequestModel
    {
        [Required(ErrorMessage = "Number of blocks to generate is required.")]
        public int BlockCount { get; set; }

        [Required(ErrorMessage = "Wallet name is required.")]
        public string WalletName { get; set; }

        [Required(ErrorMessage = "Wallet password is required.")]
        public string WalletPassword { get; set; }

        [Required(ErrorMessage = "AddToBlockchain is required.")]
        public bool AddToBlockchain { get; set; }

        public int Version { get; set; }
        public uint Nonce { get; set; }
        public uint TimeOffset { get; set; }
        public int StakingTimeout { get; set; }

        public StakeBlockRequest()
        {
            this.Version = 536870912;
            this.Nonce = 0;
            this.TimeOffset = 0;
            this.StakingTimeout = 60;
        }
    }
}
