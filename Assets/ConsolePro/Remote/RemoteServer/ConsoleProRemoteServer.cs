// Commented out: Remote Server is not needed in Editor with Console Pro 3 integration.
// Our CPAPI magic strings directly embed filter/watch commands in Debug.Log output.
// In WebGL builds, add ConsoleProRemoteServer to the scene via Tools > Console Pro > Add Remote Server.
//#define USECONSOLEPROREMOTESERVERINEDITOR

#if (!UNITY_EDITOR && DEBUG) || (UNITY_EDITOR && USECONSOLEPROREMOTESERVERINEDITOR)
#if !UNSUPPORTEDCONSOLEPROREMOTESERVER
#define USECONSOLEPROREMOTESERVER
#endif
#endif

#if UNITY_EDITOR && !USECONSOLEPROREMOTESERVER
#elif UNSUPPORTEDCONSOLEPROREMOTESERVER
#elif !USECONSOLEPROREMOTESERVER
#else
using System;
#endif

using System.Net;
using System.Net.Sockets;
using UnityEngine;

#if USECONSOLEPROREMOTESERVER
using FlyingWormConsole3.LiteNetLib;
using FlyingWormConsole3.LiteNetLib.Utils;
#endif

namespace FlyingWormConsole3
{
#if USECONSOLEPROREMOTESERVER
	[DefaultExecutionOrder(-1000)]
	public class ConsoleProRemoteServer : MonoBehaviour, INetEventListener
#else
[DefaultExecutionOrder(-1000)]
public class ConsoleProRemoteServer : MonoBehaviour
#endif
	{
		public bool useNATPunch = false;
		public int port = 51000;

#if UNITY_EDITOR && !USECONSOLEPROREMOTESERVER

#elif UNSUPPORTEDCONSOLEPROREMOTESERVER

	public void Awake()
	{
		Debug.Log("Console Pro Remote Server is not supported on this platform");
	}

#elif !USECONSOLEPROREMOTESERVER
	
	public void Awake()
	{
		Debug.Log("Console Pro Remote Server is disabled in release mode, please use a Development build or define DEBUG to use it");
	}

#else

		private const int MaxLogsPerBatch = 8;
		private const int MaxBatchesPerFrame = 8;

		private const int MaxLogMessageSize = 64000;
		private const int MaxQueueSize = 32000;

		private NetManager _netServer;
		private NetPeer _connectedPeer;
		private NetDataWriter _dataWriter;

		[SerializableAttribute]
		private class QueuedLog
		{
			public string timestamp;
			public string message;
			public string stack;
			public string logType;
		}

		private QueuedLog[] _logBuffer = new QueuedLog[MaxQueueSize];
		private int _head = 0;
		private int _tail = 0;
		private int _count = 0;

		private readonly object _logsLock = new object();

		private static ConsoleProRemoteServer _instance = null;

		void Awake()
		{
			if (_instance != null)
			{
				Destroy(gameObject);
			}

			_instance = this;

			DontDestroyOnLoad(gameObject);

			Debug.Log($"#Remote# Starting Console Pro Server on port: {port}");

			_dataWriter = new NetDataWriter();
			_netServer = new NetManager(this);
			_netServer.BroadcastReceiveEnabled = true;
			_netServer.UnconnectedMessagesEnabled = true;
			_netServer.IPv6Enabled = false;
			_netServer.UpdateTime = 15;
			_netServer.NatPunchEnabled = useNATPunch;

			_netServer.Start(port);
		}

		void OnDestroy()
		{
			if (_netServer == null)
			{
				return;
			}

			_netServer.Stop();
		}

		public void OnPeerConnected(NetPeer peer)
		{
			Debug.Log($"#Remote# Connected to {peer}");

			_connectedPeer = peer;
		}

		public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
		{
			Debug.Log($"#Remote# Disconnected from {peer}, reason: {disconnectInfo.Reason}");

			if (peer == _connectedPeer)
			{
				_connectedPeer = null;
			}
		}

		public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
		{
		}

		public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
		{
		}

		public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
		{
			if (messageType != UnconnectedMessageType.Broadcast)
			{
				return;
			}

			_netServer.SendUnconnectedMessage(new byte[] { 1 }, remoteEndPoint);
		}


		public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
		{
		}

		public void OnConnectionRequest(ConnectionRequest request)
		{
			request.AcceptIfKey("Console Pro");
		}

		void OnEnable()
		{
			Application.logMessageReceivedThreaded += LogCallback;
		}

		void OnDisable()
		{
			Application.logMessageReceivedThreaded -= LogCallback;
		}

		public void LogCallback(string logString, string stackTrace, LogType type)
		{
			if (logString.StartsWith("CPIGNORE"))
			{
				return;
			}

			QueueLog(logString, stackTrace, type);
		}

		void QueueLog(string logString, string stackTrace, LogType type)
		{
			lock (_logsLock)
			{
				if (_count >= MaxQueueSize)
				{
					return;
				}

				_logBuffer[_head] = new QueuedLog()
				{
					message = logString,
					stack = stackTrace,
					logType = type.ToString(),
					timestamp = $"[{DateTime.Now.ToString("HH:mm:ss")}]"
				};
				_head = (_head + 1) % MaxQueueSize;
				_count++;
			}
		}

		void LateUpdate()
		{
			if (_netServer == null)
			{
				return;
			}

			_netServer.PollEvents();

			if (_connectedPeer == null)
			{
				return;
			}

			int batchesSent = 0;
			lock (_logsLock)
			{
				while (_count > 0 && batchesSent < MaxBatchesPerFrame)
				{
					try
					{
						int sendCount = Math.Min(MaxLogsPerBatch, _count);
						_dataWriter.Reset();
						_dataWriter.Put(sendCount);
						for (int i = 0; i < sendCount; i++)
						{
							int index = (_tail + i) % MaxQueueSize;
							QueuedLog queuedLog = _logBuffer[index];
							_dataWriter.Put(queuedLog.timestamp, MaxLogMessageSize);
							_dataWriter.Put(queuedLog.message, MaxLogMessageSize);
							_dataWriter.Put(queuedLog.stack, MaxLogMessageSize);
							_dataWriter.Put(queuedLog.logType, MaxLogMessageSize);
							_logBuffer[index] = default;
						}
						_tail = (_tail + sendCount) % MaxQueueSize;
						_count -= sendCount;
						_connectedPeer.Send(_dataWriter, DeliveryMethod.ReliableOrdered);
						batchesSent++;
					}
					catch (Exception e)
					{
						Debug.LogError($"CPIGNORE Send error: {e.Message}");
						break;
					}
				}
			}
		}

#endif
	}
}
