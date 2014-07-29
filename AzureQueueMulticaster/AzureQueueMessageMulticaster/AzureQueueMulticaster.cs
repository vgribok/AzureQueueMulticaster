using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

using Aspectacular;

namespace AzureQueueMessageMulticaster
{
	/// <summary>
	/// Contains data necessary to connect to a write-only Azure queue.
	/// </summary>
	public abstract class AzureQueueConnection
	{
		protected AzureQueueConnection()
		{
			this.lazyQueue = new Lazy<CloudQueue>(this.InitAzureQueue);
		}

		/// <summary>
		/// A name of the ConnectionString Role setting, that specifies 
		/// how to access a queue.
		/// </summary>
		public string ConnectionStringName { get; set; }

		/// <summary>
		/// A name of the queue to be accessed.
		/// </summary>
		public string QueueName { get; set; }

		protected Lazy<CloudQueue> lazyQueue;
		protected internal CloudQueue Queue
		{
			get { return this.lazyQueue.Value; }
		}

		protected CloudQueue InitAzureQueue()
		{
			if (this.ConnectionStringName.IsBlank())
				return null;
			if (this.QueueName.IsBlank())
				return null;

			string storeConnectionString = RoleEnvironment.GetConfigurationSettingValue(this.ConnectionStringName);
			CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storeConnectionString);
			CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
			CloudQueue queue = queueClient.GetQueueReference(this.QueueName);
			queue.CreateIfNotExists();
			return queue;
		}
	}

	[Serializable]
	public class AzureDestinationQueueConnection : AzureQueueConnection
	{
	}

	[Serializable]
	public class AzureSourceQueueConnection : AzureQueueConnection
	{
		[XmlAttribute]
		public int MessageInivisibilityTimeMillisec { get; set; }

		[XmlAttribute]
		public int MaxDelayBetweenDequeueAttemptsSeconds { get; set; }
	}

	/// <summary>
	/// Represents a route between a single source queue and multiple destination queues.
	/// </summary>
	[Serializable]
	public class AzureQueueMulticastRoute : IDisposable
	{
		protected AzureQueueMonitor queueMonitor;

		// ReSharper disable once EmptyConstructor
		public AzureQueueMulticastRoute()
		{
			this.DestinationQueues = new List<AzureDestinationQueueConnection>();
		}

		public AzureSourceQueueConnection SourceQueue { get; set; }
		public List<AzureDestinationQueueConnection> DestinationQueues { get; set; }

		/// <summary>
		/// Starts Azure queue multicast relay of messages for the route.
		/// </summary>
		/// <returns></returns>
		public bool Start()
		{
			this.Stop();

			if (this.SourceQueue.Queue == null)
			{
				Trace.TraceWarning("Queue \"{0}\" failed to get initialized. Will not be polled.", this.SourceQueue.QueueName);
				return false;
			}

			if (this.DestinationQueues.IsNullOrEmpty())
			{
				Trace.TraceInformation("Destination queues for source queue \"{0}\" are not specified. Source queue will not be polled.", this.SourceQueue.QueueName);
				return false;
			}

			int initializedDestinationQueueCount = this.DestinationQueues.Count(destinationQueue => destinationQueue.Queue != null);
			Trace.TraceInformation("Initialized {0} of {1} destination queues for source queue \"{2}\".", initializedDestinationQueueCount, this.DestinationQueues.Count, this.SourceQueue.QueueName);

			lock (this)
			{
				this.queueMonitor = this.SourceQueue.Queue.RegisterMessageHandler(
					this.RelayMessages,
					this.SourceQueue.MessageInivisibilityTimeMillisec,
					this.SourceQueue.MaxDelayBetweenDequeueAttemptsSeconds,
					useAopProxyWhenAccessingQueue: true
					);
			}

			return true;
		}

		/// <summary>
		/// Receives messages from source queue and puts them into destination queues.
		/// </summary>
		/// <param name="sourceQueue"></param>
		/// <param name="messages"></param>
		protected void RelayMessages(CloudQueue sourceQueue, List<CloudQueueMessage> messages)
		{
			if (messages.IsNullOrEmpty())
				return;

			Trace.TraceInformation("Received {0} messages from queue \"{1}\". Dispatching them to {2} destination queues.", messages.Count, sourceQueue.Name, this.DestinationQueues.Count);
			//messages.ForEach(msg => Trace.TraceInformation("Received message at {0}:\r\n\"{1}\"\r\n", DateTimeOffset.Now, msg.AsString));

			IEnumerable<byte[]> messageBodies = messages.Select(msg => msg.AsBytes).ToList();

			List<Task> relays = this.DestinationQueues.ForEachAsync(destQueue => this.RelayMessagesToDestQueue(destQueue, messageBodies));
			Task.WaitAll(relays.ToArray());

			int failedCount = relays.Count(task => task.IsFaulted);
			if (failedCount < this.DestinationQueues.Count)
			{
				// None or not all relays failed.
				messages.ForEach(msg => sourceQueue.DeleteMessage(msg));
			}
		}

		private void RelayMessagesToDestQueue(AzureDestinationQueueConnection destQueue, IEnumerable<byte[]> messageBodies)
		{
			foreach (byte[] msgBody in messageBodies)
			{
				CloudQueueMessage relayMessage = new CloudQueueMessage(msgBody);
				destQueue.Queue.AddMessage(relayMessage);
			}
		}

		public void Dispose()
		{
			this.Stop();
		}

		/// <summary>
		/// Stops route's message relay.
		/// </summary>
		public void Stop()
		{
			lock (this)
			{
				if (this.queueMonitor != null)
				{
					queueMonitor.Stop();
					this.queueMonitor = null;
				}
			}
		}
	}

	[XmlRoot(ElementName = "AzureQueueMulticastRoutes")]
	public class AzureQueueMulticastRouteConfiguration : List<AzureQueueMulticastRoute>
	{
		/*
		 *  Example of the route configuration stored as XML:
		 
			<AzureQueueMulticastRoutes xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
				<AzureQueueMulticastRoute>
					<SourceQueue MessageInivisibilityTimeMillisec="10000" MaxDelayBetweenDequeueAttemptsSeconds="15">
						<ConnectionStringName>QueueStorageAccountConnectionString</ConnectionStringName>
						<QueueName>scheduletriggers</QueueName>
					</SourceQueue>
					<DestinationQueues>
						<AzureDestinationQueueConnection>
							<ConnectionStringName>QueueStorageAccountConnectionString</ConnectionStringName>
							<QueueName>destionationuno</QueueName>
						</AzureDestinationQueueConnection>
						<AzureDestinationQueueConnection>
							<ConnectionStringName>QueueStorageAccountConnectionString</ConnectionStringName>
							<QueueName>destionationdos</QueueName>
						</AzureDestinationQueueConnection>
					</DestinationQueues>
				</AzureQueueMulticastRoute>
			</AzureQueueMulticastRoutes>         
		 */

		/// <summary>
		/// Loads Azure queue multicast route configuration from XML.
		/// </summary>
		/// <param name="formatForRoleSettings">If true, CR/LFs get removed from XML so XML could be stored as a value of an Azure role setting.</param>
		/// <param name="defaultNamespace">XML default namespace.</param>
		/// <param name="settings">XML serialization settings.</param>
		/// <returns></returns>
		public string ToXml(bool formatForRoleSettings, string defaultNamespace = null, XmlWriterSettings settings = null)
		{
			if (formatForRoleSettings)
			{
				if (settings == null)
					settings = new XmlWriterSettings();

				settings.NewLineHandling = NewLineHandling.Replace;
				settings.NewLineChars = " ";

				settings.Indent = false;
				settings.Encoding = Encoding.UTF8;
				settings.OmitXmlDeclaration = true;
			}

			// ReSharper disable once InvokeAsExtensionMethod
			string xml = TypeAndRefectionExtensions.ToXml(this, defaultNamespace, settings);

			return xml;
		}

		/// <summary>
		/// Loads Azure queue multicasting relay route configuration from XML stored as a Role setting value.
		/// </summary>
		/// <param name="azureRoleSettingName"></param>
		/// <returns></returns>
		public static AzureQueueMulticastRouteConfiguration LoadFromAzureRoleSettings(string azureRoleSettingName = "AzureQueueMulticastRoutes")
		{
			if (azureRoleSettingName.IsBlank())
				azureRoleSettingName = "AzureQueueMulticastRoutes";

			string routeConfigXml = RoleEnvironment.GetConfigurationSettingValue(azureRoleSettingName);
			AzureQueueMulticastRouteConfiguration routeConfig = routeConfigXml.FromXml<AzureQueueMulticastRouteConfiguration>();

			return routeConfig;
		}

		/// <summary>
		/// Starts multicast relay of Azure queue messages for all routes.
		/// </summary>
		/// <returns>Count of routes that have started successfully.</returns>
		public int Start()
		{
			int successfulStartCount = this.Count(route => route.Start());

			if (successfulStartCount != this.Count)
				Trace.TraceWarning("Started {0} of {1} azure queue multicasting routes.", successfulStartCount, this.Count);

			return successfulStartCount;
		}

		/// <summary>
		/// Stops relaying Azure queue messages for all routes.
		/// </summary>
		public void Stop()
		{
			this.ForEach(route => route.Stop());
		}
	}
}
