using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NLog;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Client;
using MQTTnet;
using Newtonsoft.Json;
using System.Reflection;

namespace DominoShared
{
	public partial class BaseManager
	{
		protected IManagedMqttClient mqttClient;
		public string MQTTDns { get; set; }
		public string MQTTUser { get; set; }
		public string MQTTPassword { get; set; }
		public string MQTTRoot { get; set; }

		public void startMQTT()
		{
			// Setup and start a managed MQTT client.
			string clientId = Assembly.GetEntryAssembly().ManifestModule.ToString();
			MqttApplicationMessage lastwill = new MqttApplicationMessageBuilder()
													.WithTopic(string.Join('/', MQTTRoot, "available","get"))
													.WithPayload("offline")
													.WithExactlyOnceQoS()
													.WithRetainFlag()
													.Build();
			var options = new ManagedMqttClientOptionsBuilder()
				.WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
				.WithClientOptions(new MqttClientOptionsBuilder()
					.WithClientId(clientId)
					.WithTcpServer(MQTTDns)
					.WithCredentials(MQTTUser, MQTTPassword)
					.WithWillMessage(lastwill)
					.Build())
				.Build();

			mqttClient = new MqttFactory().CreateManagedMqttClient();
			mqttClient.ApplicationMessageReceived += ApplicationMessageReceived;
			mqttClient.Connected += Connected;
			mqttClient.Disconnected += Disconnected;
			LogEventRaisedHandler += LogEventRaised;

			mqttClient.StartAsync(options).GetAwaiter().GetResult();
			var waitConnected = Task.Run( async () => 
			{
				while (!mqttClient.IsConnected) await Task.Delay(500);
			});
			if (waitConnected != Task.WhenAny(waitConnected, Task.Delay(10000)).GetAwaiter().GetResult())
				throw new TimeoutException();
		}

		public async Task stopMQTT()
		{
			await mqttClient.StopAsync();
		}

		public void SendAvailable()
		{
			Publish("available/get", "online", true);
		}

		async void Connected(object sender, MqttClientConnectedEventArgs e)
		{
			logger.Debug("Mqqtclient Connected to server");

			// Subscribe to a topic
			await mqttClient.SubscribeAsync(new TopicFilterBuilder().WithTopic(MQTTRoot + "/#").Build());
		}

		private void Disconnected(object sender, MqttClientDisconnectedEventArgs e)
		{
			logger.Debug("Mqqtclient connection lost");
		}

		void ApplicationMessageReceived(object sender, MqttApplicationMessageReceivedEventArgs e)
		{
			string stVal = Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? new byte[0]);
			string log = "MQTT Received";
			log += $": Topic = {e.ApplicationMessage.Topic}";
			log += $", Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}";
			log += $", QoS = {e.ApplicationMessage.QualityOfServiceLevel}";
			log += $", Retain = {e.ApplicationMessage.Retain}";
			logger.Debug(log);

			OnMqttMessageReceived(e.ApplicationMessage.Topic, stVal);
		}

		public virtual void OnMqttMessageReceived(string topic, string payload)
		{	
			// to override
		}

		async void LogEventRaised(object sender, LogEventArgs e)
		{
			if (mqttClient == null || mqttClient.IsConnected == false) return;
			string json = JsonConvert.SerializeObject(e);
			var applicationMessage = new MqttApplicationMessageBuilder()
										.WithTopic("log/" + e.Assembly)
										.WithPayload(json)
										.WithAtLeastOnceQoS()
										.WithRetainFlag(false)
										.Build();

			await mqttClient.PublishAsync(applicationMessage);
		}

		public async void Publish(string topic, string msg, bool retain = false)
		{
			if (mqttClient == null || mqttClient.IsConnected == false) return;
			var applicationMessage = new MqttApplicationMessageBuilder()
										.WithTopic(string.Join('/',MQTTRoot,topic))
										.WithPayload(msg)
										.WithAtLeastOnceQoS()
										.WithRetainFlag(retain)
										.Build();
			string log = "MQTT Publish:";
			log += $": Topic = {applicationMessage.Topic}";
			log += $", Payload = {Encoding.UTF8.GetString(applicationMessage.Payload)}";
			log += $", QoS = {applicationMessage.QualityOfServiceLevel}";
			log += $", Retain = {applicationMessage.Retain}";
			logger.Debug(log);
			await mqttClient.PublishAsync(applicationMessage);
		}

		public static bool TopicMatch(string topic, string filter)
		{
			return MQTTnet.Server.MqttTopicFilterComparer.IsMatch(topic, filter);
		}

		public class LogEventArgs : EventArgs
		{
			public DateTime Dt { get; set; }
			public string Assembly { get; set; }
			public string Level { get; set; }
			public string Message { get; set; }
			public string Exception { get; set; }
			public string Callsite { get; set; }
		}
		public static event EventHandler<LogEventArgs> LogEventRaisedHandler;
		public static void LogMethod(string date, string level, string message, string ex, string logger, string callsite)
		{
			string name = logger.Split(".")[0];
			DateTime dt = DateTime.Parse(date);

			if (LogEventRaisedHandler != null)
				LogEventRaisedHandler(null, new LogEventArgs()
				{
					Assembly = name,
					Dt = dt,
					Level = level,
					Message = message,
					Exception = ex,
					Callsite = callsite
				});
		}
	}
}
