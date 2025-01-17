using Flurl.Http;
using Flurl.Http.Configuration;
using Headstart.API.Commands;
using Headstart.API.Commands.Zoho;
using Headstart.Common;
using Headstart.Common.Repositories;
using Headstart.Common.Services;
using Headstart.Common.Services.Zoho;
using Headstart.Jobs;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using ordercloud.integrations.cardconnect;
using ordercloud.integrations.library;
using OrderCloud.SDK;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using SendGrid;
using System;
using System.Collections.Generic;
using System.Net;

[assembly: FunctionsStartup(typeof(Startup))]
namespace Headstart.Jobs
{
	public class Startup : FunctionsStartup
	{
		public override void Configure(IFunctionsHostBuilder builder)
		{
			var connectionString = Environment.GetEnvironmentVariable(@"APP_CONFIG_CONNECTION");
			var settings = new AppSettings();
			var config = new ConfigurationBuilder()
				.AddEnvironmentVariables()
				.AddAzureAppConfiguration(connectionString)
				.Build();
			config.Bind(settings);

			var cosmosConfig = new CosmosConfig(
				settings.CosmosSettings.DatabaseName,
				settings.CosmosSettings.EndpointUri,
				settings.CosmosSettings.PrimaryKey,
				settings.CosmosSettings.RequestTimeoutInSeconds
			);

			var cosmosContainers = new List<ContainerInfo>()
			{
				new ContainerInfo()
				{
					Name = @"salesorderdetail",
					PartitionKey = @"/PartitionKey"
				},
				new ContainerInfo()
				{
					Name = @"purchaseorderdetail",
					PartitionKey = @"/PartitionKey"
				},
				new ContainerInfo()
				{
					Name = @"lineitemdetail",
					PartitionKey = @"/PartitionKey"
				},
				new ContainerInfo()
				{
					Name = @"rmas",
					PartitionKey = @"/PartitionKey"
				},
				new ContainerInfo()
				{
					Name = @"shipmentdetail",
					PartitionKey = @"/PartitionKey"
				},
				new ContainerInfo()
				{
					Name = @"productdetail",
					PartitionKey = @"/PartitionKey"
				}
			};

			// https://github.com/Polly-Contrib/Polly.Contrib.WaitAndRetry#wait-and-retry-with-jittered-back-off
			var delay = Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(30), retryCount: 3);
			var policy = HttpPolicyExtensions
				.HandleTransientHttpError()
				.OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
				.WaitAndRetryAsync(delay);

			var flurlClientFactory = new PerBaseUrlFlurlClientFactory();

			FlurlHttp.Configure(settings => settings.HttpClientFactory = new PollyFactory(policy));

			builder.Services
				.InjectOrderCloud<IOrderCloudClient>(new OrderCloudClientConfig()
				{
					ApiUrl = settings.OrderCloudSettings.ApiUrl,
					AuthUrl = settings.OrderCloudSettings.ApiUrl,
					ClientId = settings.OrderCloudSettings.MiddlewareClientId,
					ClientSecret = settings.OrderCloudSettings.MiddlewareClientSecret,
					Roles = new[]
					{
						ApiRole.FullAccess
					}
				})
				.AddCosmosDb(settings.CosmosSettings.EndpointUri, settings.CosmosSettings.PrimaryKey, settings.CosmosSettings.DatabaseName, cosmosContainers)
				.AddSingleton<IFlurlClientFactory, PerBaseUrlFlurlClientFactory>()
				.AddSingleton<IOrderCloudIntegrationsCardConnectService>(x => new OrderCloudIntegrationsCardConnectService(settings.CardConnectSettings, settings.EnvironmentSettings.Environment.ToString(), flurlClientFactory))
				.Inject<IHsCatalogCommand>()
				.Inject<IHsBuyerLocationCommand>()
				.AddSingleton<PaymentCaptureJob>()
				.AddSingleton<SendRecentOrdersJob>()
				.AddSingleton<ReceiveRecentSalesOrdersJob>()
				.AddSingleton<ReceiveProductDetailsJob>()
				.AddSingleton<ReceiveRecentPurchaseOrdersJob>()
				.AddSingleton<ReceiveRecentLineItemsJob>()
				.AddSingleton<ReceiveRecentOrdersAndShipmentsJob>()
				.AddSingleton(x => new ZohoClientConfig
				{
					ApiUrl = "https://books.zoho.com/api/v3",
					AccessToken = settings.ZohoSettings.AccessToken,
					ClientId = settings.ZohoSettings.ClientId,
					ClientSecret = settings.ZohoSettings.ClientSecret,
					OrganizationId = settings.ZohoSettings.OrgId
				})
				.Inject<IZohoClient>()
				.Inject<IZohoCommand>()
				.AddSingleton<ISendGridClient>(x => new SendGridClient(settings.SendgridSettings.ApiKey))
				.Inject<ISendgridService>()
				.Inject<ISalesOrderDetailDataRepo>()
				.Inject<IPurchaseOrderDetailDataRepo>()
				.Inject<ILineItemDetailDataRepo>()
				.Inject<IOrdersAndShipmentsDataRepo>()
				.AddSingleton(settings)
				.AddMvcCore().AddNewtonsoftJson(o =>
				{
					o.SerializerSettings.ContractResolver = new DefaultContractResolver();
					o.SerializerSettings.Converters.Add(new StringEnumConverter());
					o.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
				})
				.Services.Replace(ServiceDescriptor.Singleton(typeof(IConfiguration), config));
		}
	}
}