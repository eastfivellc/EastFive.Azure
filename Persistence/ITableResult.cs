using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Persistence
{
    public interface ITableResult
    {
        string ETag { get; }
    }

    public interface IUpdateTableResult : ITableResult
    {

    }

    internal class StorageTableResult : ITableResult
    {
        protected TableResult tableResult;

        public StorageTableResult(TableResult tableResult)
        {
            this.tableResult = tableResult;
        }

        public string ETag => this.tableResult.Etag;
    }

    internal class StorageUpdateTableResult : StorageTableResult, IUpdateTableResult
    {
        public StorageUpdateTableResult(TableResult tableResult)
            : base(tableResult)
        {
            this.tableResult = tableResult;
        }
    }
}
