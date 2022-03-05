﻿using System;
using OrderCloud.SDK;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Headstart.Common.Helpers
{
    public static class ClientHelper
    {
        private static readonly ConcurrentDictionary<string, OrderCloudClient> storageConnectionByClientID = new ConcurrentDictionary<string, OrderCloudClient>();

        public static async Task RunAction(OrderCloudClientConfig config, Func<OrderCloudClient, Task> action)
        {
            // If another client is needed, use this to store it. This ensures only one client is created and reused.
            storageConnectionByClientID.GetOrAdd(config.ClientId, x => new OrderCloudClient(config));
            try
            {
                await action(storageConnectionByClientID[config.ClientId]);
            }
            catch (Exception ex)
            {
                throw new Exception($@"Unable to get OC connection for client: {config.ClientId}, {ex.Message}");
            }
        }
    }
}