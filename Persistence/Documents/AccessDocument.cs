﻿using EastFive.Serialization;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EastFive.Security.SessionServer.Persistence.Documents
{
    internal class AccessDocument : Microsoft.WindowsAzure.Storage.Table.TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id
        {
            get { return Guid.Parse(this.RowKey); }
        }
        
        public string Method { get; set; }

        public Guid LookupId { get; set; }

        public byte[] ExtraParams { get; set; }

        internal IDictionary<string, string> GetExtraParams()
        {
            return ExtraParams.FromByteArray(
                (keyBytes) => System.Text.Encoding.UTF8.GetString(keyBytes),
                (valueBytes) => System.Text.Encoding.UTF8.GetString(valueBytes));
        }

        internal void SetExtraParams(IDictionary<string, string> extraParams)
        {
            ExtraParams = extraParams.ToByteArray(
                (key) => System.Text.Encoding.UTF8.GetBytes(key),
                (value) => System.Text.Encoding.UTF8.GetBytes(value));
        }
    }
}
