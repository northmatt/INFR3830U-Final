using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace Server {
    struct ClientInfo {
        public byte id;
        public string[] name;
        public Socket TCPSocket;
        public EndPoint UDPEndpoint;
        public float[] position;
        public float[] rotation;
        public float[] velocity;
        public float[] acceleration;
        public byte[] score;
    }

    [Flags] enum DirtyFlag {
        None = 0,
        Transform = 1,
        Message = 2
    }

    public enum ServerNetworkCalls : byte {
        TCPSetClientName = 0,
        TCPClientMessage = 1,
        TCPClientScore = 2,
        UDPClientConnection = 0 + 0x10,
        UDPClientTransform = 1 + 0x10
    }

    public enum ClientNetworkCalls : byte {
        TCPClientConnection = 0,
        TCPClientDisconnection = 1,
        TCPClientTransform = 2,
        TCPClientsTransform = 3,
        TCPSetClientName = 4,
        TCPClientMessage = 5,
        TCPClientScore = 6,
        UDPClientsTransform = 0 + 0x10
    }

    class Program {
        private static byte[] receiveBuffer = new byte[1024];
        private static Socket TCPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private static Socket UDPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private static EndPoint UDPReceiveEndPoint;
        private static ClientInfo tempClientInfo;
        private static List<ClientInfo> clientInfoList = new List<ClientInfo>();
        private static bool shutdownServer = false;
        private static DirtyFlag dirtyFlag = DirtyFlag.None;
        private static int maxExtraSend = 10;
        private static int curExtraSend = 0;

        static void Main(string[] args) {
            StartServer("127.0.0.1", 8888);

            Thread sendThread = new Thread(new ThreadStart(SendLoop));
            sendThread.Name = "SendThread";
            sendThread.Start();

            Console.ReadLine();

            ShutdownServer();

            Console.ReadLine();
        }

        public static void StartServer(string address, int port) {
            Console.WriteLine("Press Return to close server.");
            Console.WriteLine("Starting Server...");
            TCPSocket.Bind(new IPEndPoint(IPAddress.Parse(address), port));

            UDPReceiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
            UDPSocket.Bind(new IPEndPoint(IPAddress.Parse(address), port + 1));

            TCPSocket.Listen(10);
            //Timeout is 200ms
            UDPSocket.SendTimeout = 200;
            UDPSocket.ReceiveTimeout = 200;
            TCPSocket.SendTimeout = 200;
            TCPSocket.ReceiveTimeout = 200;
            //Buffersize is 2x data needed to be sent
            UDPSocket.SendBufferSize = 1024;
            UDPSocket.ReceiveBufferSize = 1024;
            TCPSocket.SendBufferSize = 1024;
            TCPSocket.ReceiveBufferSize = 1024;

            tempClientInfo.TCPSocket = null;
            tempClientInfo.UDPEndpoint = null;

            TCPSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
            UDPSocket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, 0, ref UDPReceiveEndPoint, new AsyncCallback(ReceiveUDPCallback), UDPSocket);
        }

        public static void ShutdownServer() {
            //Release server socket resources
            try {
                shutdownServer = true;

                foreach (ClientInfo client in clientInfoList) {
                    DisconnectClient(client);
                }

                TCPSocket.Close();
                UDPSocket.Close();

                Console.WriteLine("Stopped Server");

                return;
            }
            catch (System.Exception exc) {
                Console.WriteLine("Exception: " + exc.ToString());
            }

            //Release server socket resources
            TCPSocket.Close();
            UDPSocket.Close();
            Console.WriteLine("Stopped Server");

            return;
        }

        public static void DisconnectClient(ClientInfo client, bool removeFromList = false) {
            try {
                string clientIp = client.TCPSocket.RemoteEndPoint.ToString();
                //Console.WriteLine("Disconnecting Client " + clientIp);

                client.TCPSocket.Shutdown(SocketShutdown.Both);
                client.TCPSocket.Close();

                if (removeFromList)
                    clientInfoList.Remove(client);

                Console.WriteLine("Disconnected Client {0} (TCP {1})", client.id, clientIp);
            }
            catch (System.Exception exc) {
                Console.WriteLine("Exception: " + exc.ToString());
            }
            
        }

        public static void DisconnectClient(Socket tcpSocket) {
            try {
                string clientIp = tcpSocket.RemoteEndPoint.ToString();
                //Console.WriteLine("Disconnecting Client " + clientIp);

                ClientInfo client = FindClient(tcpSocket);

                if (client.TCPSocket == null || client.UDPEndpoint == null) {
                    Console.WriteLine("Cant disconnect client, client {0} not found", clientIp);
                    return;
                }

                DisconnectClient(client, true);

                //Tell other clients about disconnection
                byte[] sendBuffer = new byte[4];
                BufferSetup(sendBuffer, client.id, true, false);
                SendNetworkCallback(clientInfoList, sendBuffer);
            }
            catch (System.Exception exc) {
                Console.WriteLine("Exception: " + exc.ToString());
            }
        }

        private static void AcceptCallback(IAsyncResult result) {
            if (shutdownServer)
                return;

            try {
                Socket clientTCPSocket = TCPSocket.EndAccept(result);

                IPEndPoint TCPEndPoint = (IPEndPoint)clientTCPSocket.RemoteEndPoint;
                //Console.WriteLine("TCP Client {0}:{1} connected", TCPEndPoint.Address.ToString(), TCPEndPoint.Port.ToString());
                //Timeout is 200ms
                clientTCPSocket.SendTimeout = 200;
                clientTCPSocket.ReceiveTimeout = 200;
                //Buffersize is 2x data needed to be sent
                clientTCPSocket.SendBufferSize = 1024;
                clientTCPSocket.ReceiveBufferSize = 1024;

                tempClientInfo.TCPSocket = clientTCPSocket;

                Thread clientInfoThread = new Thread(new ThreadStart(SetupClientInfo));
                clientInfoThread.Name = "serverClientInfoThread";
                clientInfoThread.Start();
            }
            catch (System.Exception exc) {
                Console.WriteLine("Exception: " + exc.ToString());
            }
        }

        private static void ReceiveTCPCallback(IAsyncResult result) {
            if (shutdownServer)
                return;

            Socket socket = (Socket)result.AsyncState;
            int bufferLength = 0;

            try {
                bufferLength = socket.EndReceive(result);
            }
            catch (SocketException exc) {
                Console.WriteLine("Socket Exception: " + exc.ToString());
            }
            catch (Exception exc) {
                Console.WriteLine("Exception: " + exc.ToString());
            }

            //Handle Disconnect
            if (bufferLength == 0) {
                DisconnectClient(socket);
                return;
            }

            byte[] data = new byte[bufferLength];
            Array.Copy(receiveBuffer, data, bufferLength);
            socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, 0, new AsyncCallback(ReceiveTCPCallback), socket);

            //First byte is the "protocol"/"type" of data sent
            switch ((ServerNetworkCalls)data[0]) {
                case ServerNetworkCalls.TCPSetClientName:
                    ClientInfo client1 = FindClient(data[1]);
                    if (client1.TCPSocket == null) {
                        Console.WriteLine("Cant rename client, client {0} not found", data[1]);
                        break;
                    }

                    client1.name[0] = Encoding.ASCII.GetString(data, 4, bufferLength - 4);

                    Console.WriteLine("Renamed client {0} to {1}", data[1], FindClient(data[1]).name[0]);

                    data[0] = (byte)ClientNetworkCalls.TCPSetClientName;
                    SendNetworkCallback(clientInfoList, data);

                    break;
                case ServerNetworkCalls.TCPClientMessage:
                    string message = Encoding.ASCII.GetString(data, 4, bufferLength - 4);
                    message = FindClient(data[1]).name[0] + ": " + message;
                    Console.WriteLine(message);

                    data[0] = (byte)ClientNetworkCalls.TCPClientMessage;
                    SendNetworkCallback(clientInfoList, data);
                    break;
                case ServerNetworkCalls.TCPClientScore:
                    if (bufferLength != 4)
                        break;

                    ClientInfo client2 = FindClient(data[1]);
                    if (client2.TCPSocket == null) {
                        Console.WriteLine("Cant set score, client {0} not found", data[1]);
                        break;
                    }
                    client2.score[0] = data[2];

                    data[0] = (byte)ClientNetworkCalls.TCPClientScore;
                    SendNetworkCallback(clientInfoList, data);
                    break;
                default:
                    break;
            }
        }

        private static void ReceiveUDPCallback(IAsyncResult result) {
            if (shutdownServer)
                return;

            Socket socket = (Socket)result.AsyncState;
            int bufferLength = 0;

            try {
                bufferLength = socket.EndReceiveFrom(result, ref UDPReceiveEndPoint);
            }
            catch (System.Exception exc) {
                Console.WriteLine("Exception: " + exc.ToString());
                socket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, 0, ref UDPReceiveEndPoint, new AsyncCallback(ReceiveUDPCallback), socket);
                return;
            }

            byte[] data = new byte[bufferLength];
            Array.Copy(receiveBuffer, data, bufferLength);
            socket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, 0, ref UDPReceiveEndPoint, new AsyncCallback(ReceiveUDPCallback), socket);

            //First byte is the "protocol"/"type" of data sent
            switch ((ServerNetworkCalls)data[0]) {
                case ServerNetworkCalls.UDPClientConnection:
                    if (bufferLength != 4)
                        break;

                    if (tempClientInfo.TCPSocket == null || tempClientInfo.UDPEndpoint != null || data[1] != tempClientInfo.id)
                        break;

                    IPEndPoint UDPEP = (IPEndPoint)UDPReceiveEndPoint;
                    tempClientInfo.UDPEndpoint = UDPEP;
                    //Console.WriteLine("UDP Client {0}:{1} connected", test.Address, test.Port);

                    break;
                case ServerNetworkCalls.UDPClientTransform:
                    if (bufferLength != 40)
                        break;

                    ClientInfo client = FindClient(data[1]);
                    if (client.TCPSocket == null)
                        break;

                    client.position[0] = BitConverter.ToSingle(data, 4);
                    client.position[1] = BitConverter.ToSingle(data, 8);
                    client.position[2] = BitConverter.ToSingle(data, 12);
                    client.velocity[0] = BitConverter.ToSingle(data, 16);
                    client.velocity[1] = BitConverter.ToSingle(data, 20);
                    client.velocity[2] = BitConverter.ToSingle(data, 24);
                    client.acceleration[0] = BitConverter.ToSingle(data, 28);
                    client.acceleration[1] = BitConverter.ToSingle(data, 32);
                    client.acceleration[2] = BitConverter.ToSingle(data, 36);

                    dirtyFlag = DirtyFlag.Transform;
                    //Console.WriteLine("Client {0} with UDP IP {1}:{2} has new pos of {3}, {4}, {5}", client.id, UDPEP.Address, UDPEP.Port, client.position[0], client.position[1], client.position[2]);
                    break;
                default:
                    break;
            }
        }

        private static void SendNetworkCallback(ClientInfo client, byte[] sendBuffer) {
            if ((sendBuffer[0] & 0x10) == 0x10)
                UDPSocket.BeginSendTo(sendBuffer, 0, sendBuffer.Length, 0, client.UDPEndpoint, new AsyncCallback(SendUDPCallback), UDPSocket); 
            else
                client.TCPSocket.BeginSend(sendBuffer, 0, sendBuffer.Length, 0, new AsyncCallback(SendTCPCallback), client.TCPSocket);
        }

        private static void SendNetworkCallback(List<ClientInfo> clients, byte[] sendBuffer) {
            foreach (ClientInfo client in clients) {
                SendNetworkCallback(client, sendBuffer);
            }
        }

        private static void SendTCPCallback(IAsyncResult result) {
            if (shutdownServer)
                return;

            Socket socket = (Socket)result.AsyncState;
            socket.EndSend(result);
        }

        private static void SendUDPCallback(IAsyncResult result) {
            if (shutdownServer)
                return;

            Socket socket = (Socket)result.AsyncState;
            socket.EndSendTo(result);
        }

        private static void SetupClientInfo() {
            //Establish a TCP and UDP connection to get TCP/UDP ports of a client
            byte[] sendBuffer = new byte[4];
            BufferSetup(sendBuffer, FindLowestAvailableClientID(), false, false);
            SendNetworkCallback(tempClientInfo, sendBuffer);

            tempClientInfo.id = sendBuffer[1];

            //Wait for UDP packet
            byte counter = 0;
            while (tempClientInfo.TCPSocket != null && tempClientInfo.UDPEndpoint != null || counter > 50) {
                Thread.Sleep(100);
                counter++;
            }

            Thread.Sleep(100);

            if (tempClientInfo.TCPSocket != null && tempClientInfo.UDPEndpoint != null) {
                IPEndPoint TCPEP = (IPEndPoint)tempClientInfo.TCPSocket.RemoteEndPoint;
                IPEndPoint UDPEP = (IPEndPoint)tempClientInfo.UDPEndpoint;
                Console.WriteLine("Client {0} on IP {1} with TCP port {2} and UDP port {3} has connected", tempClientInfo.id, TCPEP.Address, TCPEP.Port, UDPEP.Port);

                tempClientInfo.name = new string[] { "C" };
                tempClientInfo.position = new float[] { 0f, 0f, 0f };
                tempClientInfo.rotation = new float[] { 0f, 0f, 0f };
                tempClientInfo.velocity = new float[] { 0f, 0f, 0f };
                tempClientInfo.acceleration = new float[] { 0f, 0f, 0f };
                tempClientInfo.score = new byte[] { 0 };

                //Tell new client what other clients exist on server
                sendBuffer = new byte[8 + clientInfoList.Count * 16];
                BufferSetup(sendBuffer, clientInfoList, true);
                SendNetworkCallback(tempClientInfo, sendBuffer);

                Thread.Sleep(100);

                //Send names of all clients
                foreach (ClientInfo curClient in clientInfoList) {
                    sendBuffer = new byte[Math.Min(curClient.name[0].Length + 4, 64)];
                    BufferSetup(sendBuffer, curClient.id, curClient.name[0]);
                    SendNetworkCallback(tempClientInfo, sendBuffer);
                    Thread.Sleep(50);
                }

                //Tell other clients new client joined
                sendBuffer = new byte[16];
                BufferSetup(sendBuffer, tempClientInfo);
                SendNetworkCallback(clientInfoList, sendBuffer);

                clientInfoList.Add(tempClientInfo);
                tempClientInfo.TCPSocket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, 0, new AsyncCallback(ReceiveTCPCallback), tempClientInfo.TCPSocket);

                Thread.Sleep(100);

                //Tell client the setup is finished
                sendBuffer = new byte[4];
                BufferSetup(sendBuffer, tempClientInfo.id, false, true);
                SendNetworkCallback(tempClientInfo, sendBuffer);
            }
            else if (tempClientInfo.TCPSocket != null) {
                IPEndPoint TCPEP = (IPEndPoint)tempClientInfo.TCPSocket.RemoteEndPoint;
                Console.WriteLine("Client {0} with TCP IP {1}:{2} failed to make a UDP connection", tempClientInfo.id, TCPEP.Address, TCPEP.Port);
                DisconnectClient(tempClientInfo);
            }
            else {
                Console.WriteLine("Client failed to connect");
            }


            tempClientInfo.id = 0;
            tempClientInfo.name = null;
            tempClientInfo.TCPSocket = null;
            tempClientInfo.UDPEndpoint = null;
            tempClientInfo.position = null;
            tempClientInfo.rotation = null;
            tempClientInfo.velocity = null;
            tempClientInfo.acceleration = null;
            tempClientInfo.score = null;

            TCPSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
        }
       
        private static void SendLoop() {
            long currentTime = 0;
            long previousTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            long timeWait = 1000 / 20 - 4; //Windows scheduler cant really wait that accurately
            long deltaTime = 0;

            while (!shutdownServer) {
                currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                //Find the amount of time that has passed since this function was last called
                long workTime = currentTime - previousTime;

                //If the amount of time that has passed is smaller than the time to wait it'll find out how long it needs to wait and stop the program from running for a certain amoount of time
                if (workTime < timeWait)
                    Thread.Sleep((int)(timeWait - workTime));

                //Gets deltaTime by looking at difference of current time and previous time
                currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                deltaTime = currentTime - previousTime;
                previousTime = currentTime;

                if ((dirtyFlag & DirtyFlag.Transform) == DirtyFlag.Transform)
                    curExtraSend = maxExtraSend;

                if (curExtraSend == 0)
                    continue;

                dirtyFlag = DirtyFlag.None;
                --curExtraSend;

                byte[] sendBuffer = new byte[8 + clientInfoList.Count * 40];
                BufferSetup(sendBuffer, clientInfoList, false);

                SendNetworkCallback(clientInfoList, sendBuffer);
            }
        }

        private static byte FindLowestAvailableClientID() {
            byte lowestAvailableID = 0;

            //Start at an ID of 0, stop if "lowestAvailableID" differs from "i"
            //Increase "lowestAvailableID" if any ID matches occur
            //"lowestAvailableID" can only differ from "i" if no matches occur
            for (byte i = lowestAvailableID; i == lowestAvailableID && i < byte.MaxValue; ++i) {
                foreach (ClientInfo client in clientInfoList) {
                    if (client.id == i) {
                        ++lowestAvailableID;
                        break;
                    }
                }
            }

            return lowestAvailableID;
        }

        private static ClientInfo FindClient(Socket tcpSocket) {
            ClientInfo client = new ClientInfo();
            client.id = 0;
            client.name = null;
            client.TCPSocket = null;
            client.UDPEndpoint = null;
            client.position = null;
            client.rotation = null;
            client.velocity = null;
            client.acceleration = null;
            client.score = null;

            foreach (ClientInfo curClient in clientInfoList) {
                if (curClient.TCPSocket == tcpSocket) {
                    client = curClient;
                    break;
                }
            }

            return client;
        }

        private static ClientInfo FindClient(byte clientId) {
            ClientInfo client = new ClientInfo();
            client.id = 0;
            client.name = null;
            client.TCPSocket = null;
            client.UDPEndpoint = null;
            client.position = null;
            client.rotation = null;
            client.velocity = null;
            client.acceleration = null;
            client.score = null;

            foreach (ClientInfo curClient in clientInfoList) {
                if (curClient.id == clientId) {
                    client = curClient;
                    break;
                }
            }

            return client;
        }

        private static void BufferSetup(byte[] buffer, byte id, bool disconnect, bool finishSetup) {
            //(dis)connection with ID, TCP
            buffer[0] = disconnect ? (byte)ClientNetworkCalls.TCPClientDisconnection : (byte)ClientNetworkCalls.TCPClientConnection;
            buffer[1] = id;
            buffer[2] = finishSetup ? (byte)1 : (byte)0;
        }

        private static void BufferSetup(byte[] buffer, List<ClientInfo> clientInfoList, bool useTCP) {
            //Send Pos, TCP/UDP
            buffer[0] = useTCP ? (byte)ClientNetworkCalls.TCPClientsTransform : (byte)ClientNetworkCalls.UDPClientsTransform;
            BitConverter.GetBytes(clientInfoList.Count).CopyTo(buffer, 4);

            int offset = 0;
            for (int index = 0; index < clientInfoList.Count; ++index) {
                offset = index * (useTCP ? 16 : 40);
                buffer[8 + offset] = clientInfoList[index].id;
                BitConverter.GetBytes(clientInfoList[index].position[0]).CopyTo(buffer, 12 + offset);
                BitConverter.GetBytes(clientInfoList[index].position[1]).CopyTo(buffer, 16 + offset);
                BitConverter.GetBytes(clientInfoList[index].position[2]).CopyTo(buffer, 20 + offset);

                if (useTCP) {
                    buffer[9 + offset] = clientInfoList[index].score[0];
                }
                else {
                    BitConverter.GetBytes(clientInfoList[index].velocity[0]).CopyTo(buffer, 24 + offset);
                    BitConverter.GetBytes(clientInfoList[index].velocity[1]).CopyTo(buffer, 28 + offset);
                    BitConverter.GetBytes(clientInfoList[index].velocity[2]).CopyTo(buffer, 32 + offset);
                    BitConverter.GetBytes(clientInfoList[index].acceleration[0]).CopyTo(buffer, 36 + offset);
                    BitConverter.GetBytes(clientInfoList[index].acceleration[1]).CopyTo(buffer, 40 + offset);
                    BitConverter.GetBytes(clientInfoList[index].acceleration[2]).CopyTo(buffer, 44 + offset);
                }
            }
        }

        private static void BufferSetup(byte[] buffer, ClientInfo clientInfo) {
            //Send Pos, TCP
            buffer[0] = (byte)ClientNetworkCalls.TCPClientTransform;
            buffer[1] = clientInfo.id;
            BitConverter.GetBytes(clientInfo.position[0]).CopyTo(buffer, 4);
            BitConverter.GetBytes(clientInfo.position[1]).CopyTo(buffer, 8);
            BitConverter.GetBytes(clientInfo.position[2]).CopyTo(buffer, 12);
        }

        private static void BufferSetup(byte[] buffer, byte id, string clientName) {
            //Send Pos, TCP
            buffer[0] = (byte)ClientNetworkCalls.TCPSetClientName;
            buffer[1] = id;
            Encoding.ASCII.GetBytes(clientName, 0, clientName.Length, buffer, 4);
        }
    }
}
