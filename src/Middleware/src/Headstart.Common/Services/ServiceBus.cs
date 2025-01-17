﻿using System;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Sitecore.Foundation.SitecoreExtensions.Extensions;
using SitecoreExtensions = Sitecore.Foundation.SitecoreExtensions.Extensions;

namespace Headstart.Common.Services
{
	public interface IServiceBus
	{
		Task SendMessage<T>(string queueName, T message, double? afterMinutes = null);
		Task SendMessageBatchToTopicAsync(string queueName, Queue<ServiceBusMessage> messages);
	}

	public class ServiceBus : IServiceBus
	{
		private readonly ConcurrentDictionary<string, ServiceBusSender> senders = new ConcurrentDictionary<string, ServiceBusSender>();
		private readonly ServiceBusClient _client;
		private readonly AppSettings _settings;

		/// <summary>
		/// The IOC based constructor method for the ServiceBus class object with Dependency Injection
		/// </summary>
		/// <param name="settings"></param>
		public ServiceBus(AppSettings settings)
		{
			try
			{
				_settings = settings;
				_client = new ServiceBusClient(settings.ServiceBusSettings.ConnectionString);
			}
			catch (Exception ex)
			{
				LoggingNotifications.LogApiResponseMessages(_settings.LogSettings, SitecoreExtensions.Helpers.GetMethodName(), "",
					LoggingNotifications.GetExceptionMessagePrefixKey(), true, ex.Message, ex.StackTrace, ex);
			}
		}

		/// <summary>
		/// Public re-usable SendMessage task method
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="queueName"></param>
		/// <param name="message"></param>
		/// <param name="afterMinutes"></param>
		/// <returns></returns>
		public async Task SendMessage<T>(string queueName, T message, double? afterMinutes = null)
		{
			try
			{
				var sender = senders.GetOrAdd(queueName, _client.CreateSender(queueName));
				var messageString = JsonConvert.SerializeObject(message);
				var messageBytes = Encoding.UTF8.GetBytes(messageString);
				if (afterMinutes == null)
				{
					// send message immediately
					await sender.SendMessageAsync(new ServiceBusMessage(messageBytes));
				}
				else
				{
					// send message after x minutes
					DateTime afterMinutesUtc = DateTime.UtcNow.AddMinutes((double)afterMinutes);
					await sender.SendMessageAsync(new ServiceBusMessage(messageBytes) { ScheduledEnqueueTime = afterMinutesUtc });
				}
			}
			catch (Exception ex)
			{
				LoggingNotifications.LogApiResponseMessages(_settings.LogSettings, SitecoreExtensions.Helpers.GetMethodName(), "",
					LoggingNotifications.GetExceptionMessagePrefixKey(), true, ex.Message, ex.StackTrace, ex);
			}
		}

		/// <summary>
		/// Public re-usable SendMessageBatchToTopicAsync task method
		/// </summary>
		/// <param name="topicName"></param>
		/// <param name="messages"></param>
		/// <returns></returns>
		public async Task SendMessageBatchToTopicAsync(string topicName, Queue<ServiceBusMessage> messages)
		{
			try
			{
				var sender = senders.GetOrAdd(topicName, _client.CreateSender(topicName));
				var messageCount = messages.Count;
				while (messages.Count > 0)
				{
					using var messageBatch = await sender.CreateMessageBatchAsync();

					if (messageBatch.TryAddMessage(messages.Peek()))
					{
						messages.Dequeue();
					}
					else
					{
						throw new Exception($@"Message {messageCount - messages.Count} is too large and cannot be sent.");
					}

					while (messages.Count > 0 && messageBatch.TryAddMessage(messages.Peek()))
					{
						messages.Dequeue();
					}
					await sender.SendMessagesAsync(messageBatch);
				}
				var message = $@"Sent a batch of {messageCount} messages to the topic: {topicName}.";
				LoggingNotifications.LogApiResponseMessages(_settings.LogSettings, SitecoreExtensions.Helpers.GetMethodName(), message,
					LoggingNotifications.GetExceptionMessagePrefixKey(), false);
			}
			catch (Exception ex)
			{
				LoggingNotifications.LogApiResponseMessages(_settings.LogSettings, SitecoreExtensions.Helpers.GetMethodName(), "",
					LoggingNotifications.GetExceptionMessagePrefixKey(), true, ex.Message, ex.StackTrace, ex);
			}
		}
	}
}