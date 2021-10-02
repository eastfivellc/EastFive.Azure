using System;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using EastFive;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Security.SessionServer;
using EastFive.Security;
using EastFive.Linq.Async;
using EastFive.Api.Azure.Monitoring;

namespace EastFive.Azure.Monitoring
{
    public struct MonitoringInfo
    {
        public Guid Id;
        public Guid AuthenticationId;
        public DateTime Time;
        public string Method;
        public string Controller;
        public string Content;
    }

    public class Monitoring
    {
        //private Context context;
        //private Security.SessionServer.Persistence.DataContext dataContext;

        //internal Monitoring(Context context, Security.SessionServer.Persistence.DataContext dataContext)
        //{
        //    this.dataContext = dataContext;
        //    this.context = context;
        //}

        //public Security.SessionServer.Context AzureContext
        //{
        //    get
        //    {
        //        return new EastFive.Security.SessionServer.Context(
        //            () => new EastFive.Security.SessionServer.Persistence.DataContext(
        //                EastFive.Azure.AppSettings.ASTConnectionStringKey));
        //    }
        //}

        //public Task<TResult> GetByMonthAsync<TResult>(DateTime month,
        //    Func<MonitoringInfo[], TResult> onSuccess)
        //{
        //    return MonitoringDocument.GetByMonthAsync(month, AzureContext.DataContext.AzureStorageRepository,
        //        (monitoringItems) => onSuccess(monitoringItems.Select(item => Convert(item)).ToArray()));
        //}

        //private static MonitoringInfo Convert(MonitoringDocument doc)
        //{
        //    return new MonitoringInfo
        //    {
        //        Id = doc.id,
        //        AuthenticationId = doc.AuthenticationId,
        //        Time = doc.Time,
        //        Method = doc.Method,
        //        Controller = doc.Controller,
        //        Content = doc.Content
        //    };
        //}
    }
}
