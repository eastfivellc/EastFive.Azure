﻿using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using EastFive.Azure.Auth;
using EastFive.Security.SessionServer;

namespace EastFive.Api.Azure.Credentials
{
    public interface IProvideAccountInformation
    {
        Task<TResult> CreateAccount<TResult>(
                string subject, IDictionary<string, string> extraParameters,
                Authentication authentication, Authorization authorization,
                EastFive.Api.Azure.AzureApplication webApiApplication,
            Func<Guid, TResult> onCreatedMapping,
            Func<TResult> onNoChange);
    }
}
