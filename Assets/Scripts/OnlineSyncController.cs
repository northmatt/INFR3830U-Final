using System.Collections;
using System.Collections.Generic;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public enum ConnectionType {
    Send,
    Recieve
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

public class OnlineSyncController : MonoBehaviour {
    public ConnectionType typeA; //No good and descritptive name?? Amazing
    public byte clientId = 0;
    public string clientName = "";
    public byte clientScore = 0;

    private Vector3 position = Vector3.zero;
    private Vector3 positionPrev = Vector3.zero;
    private Vector3 velocity = Vector3.zero;
    private Vector3 velocityPrev = Vector3.zero;
    private Vector3 acceleration = Vector3.zero;
    private float lastTimePosRecieved = -1f;
    private float maxVelocity = -1f;
    private bool StartedCoroutine = false;

    public static bool ConnectingToServer = false;
    public static bool ConnectedToServer = false;

    private static Socket TCPSocket;
    private static Socket UDPSocket;
    private static EndPoint UDPEndPoint;
    private static byte[] receiveBuffer = new byte[1024];
    //bool ConnectedToServerPartial

    private void Start() {
        position = transform.position;
        maxVelocity = GameController.instance.player.GetComponent<PlayerController>().velocity * 1.5f;
        clientName = "C";
    }

    private void FixedUpdate() {
        if (!StartedCoroutine && ConnectedToServer && !ConnectingToServer && typeA == ConnectionType.Send) {
            StartedCoroutine = true;
            StartCoroutine(SendPosition());
        }
    }

    private void Update() {
        if (typeA == ConnectionType.Recieve) {
            float timeElapsedSincePosRecieved = (lastTimePosRecieved == -1f ? 0f : Time.unscaledTime - lastTimePosRecieved);
            Vector3 targetPos = Vector3.Lerp(positionPrev, position, 0.75f) + velocity * timeElapsedSincePosRecieved + acceleration * timeElapsedSincePosRecieved * timeElapsedSincePosRecieved;
            Vector3 targetVel = (targetPos - transform.position);
            if (targetVel.magnitude > maxVelocity * Time.unscaledDeltaTime)
                targetVel = targetVel.normalized * maxVelocity * Time.unscaledDeltaTime;

            transform.Translate(targetVel);
        }
    }

    public static void ConnectToServer(string address, int TCPport) {
        if (ConnectingToServer || ConnectedToServer)
            return;

        Debug.Log("Attempting Connection");
        ConnectingToServer = true;

        TCPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        UDPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        TCPSocket.BeginConnect(IPAddress.Parse(address), TCPport, new AsyncCallback(BeginConnectCallback), TCPSocket);
    }

    public static bool DisconnectFromServer() {
        //Release socket resources
        try {
            bool shouldQuitGame = ConnectedToServer && !GameController.instance.gameQuitting;

            if (ConnectedToServer || TCPSocket.Connected) {
                ConnectedToServer = false;
                TCPSocket.Shutdown(SocketShutdown.Both);
            }
            TCPSocket.Close();
            UDPSocket.Close();

            Debug.Log("Disconnected From Server");

            if (shouldQuitGame)
                GameController.QuitGame();

            return true;
        }
        catch (SocketException exc) {
            Debug.Log("SocketException: " + exc.ToString());
        }
        catch (System.Exception exc) {
            Debug.Log("OtherException: " + exc.ToString());
        }

        //Release socket resources
        TCPSocket.Close();
        UDPSocket.Close();
        Debug.Log("Disconnected From Server");

        if (!GameController.instance.gameQuitting)
            GameController.QuitGame();

        return false;
    }

    private static void BeginConnectCallback(IAsyncResult result) {
        Socket socket = (Socket)result.AsyncState;

        //Setup client socket
        try {
            socket.EndConnect(result);

            //Setup endpoint
            IPEndPoint TCPEndPoint = (IPEndPoint)socket.RemoteEndPoint;
            UDPEndPoint = new IPEndPoint(TCPEndPoint.Address, TCPEndPoint.Port + 1);
            Debug.Log("TCP Connection Established to " + TCPEndPoint.Address.ToString() + ":" + TCPEndPoint.Port);
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

            TCPSocket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, 0, new AsyncCallback(ReceiveTCPCallback), TCPSocket);
            UDPSocket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, 0, ref UDPEndPoint, new AsyncCallback(ReceiveUDPCallback), UDPSocket);

            ConnectedToServer = true;
        }
        catch (System.ArgumentNullException exc) {
            Debug.Log("ArgumentNullException: " + exc.ToString());
            DisconnectFromServer();
        }
        catch (SocketException exc) {
            Debug.Log("SocketException: " + exc.ToString());
            DisconnectFromServer();
        }
        catch (System.Exception exc) {
            Debug.Log("OtherException: " + exc.ToString());
            DisconnectFromServer();
        }

        ConnectingToServer = false;
    }

    private static void ReceiveTCPCallback(IAsyncResult result) {
        Socket socket = (Socket)result.AsyncState;
        int bufferLength = 0;

        if (!ConnectedToServer)
            return;

        try {
            bufferLength = socket.EndReceive(result);
        }
        catch (SocketException exc) {
            Debug.Log("Socket Exception: " + exc.ToString());
        }
        catch (Exception exc) {
            Debug.Log("Exception: " + exc.ToString());
        }

        //Handle Disconnect
        if (bufferLength == 0) {
            Debug.Log("Buffer length zero, disconnecting");
            DisconnectFromServer();
            return;
        }

        byte[] data = new byte[bufferLength];
        Array.Copy(receiveBuffer, data, bufferLength);
        socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, 0, new AsyncCallback(ReceiveTCPCallback), socket);

        //First byte indicates the type of data sent
        switch ((ClientNetworkCalls)data[0]) {
            case ClientNetworkCalls.TCPClientConnection:
                if (bufferLength != 4) {
                    Debug.Log("Incorrect Buffer Length");
                    break;
                }

                byte[] sendBuffer = new byte[4];
                BufferSetup(sendBuffer, data[1]);
                SendNetworkCallback(sendBuffer);

                GameController.instance.AddCommand(data);
                break;
            case ClientNetworkCalls.TCPClientDisconnection:
                if (bufferLength != 4) {
                    Debug.Log("Incorrect Buffer Length");
                    break;
                }

                GameController.instance.AddCommand(data);
                break;
            case ClientNetworkCalls.TCPClientTransform:
                if (bufferLength != 16) {
                    Debug.Log("Incorrect Buffer Length");
                    break;
                }

                GameController.instance.AddCommand(data);
                break;
            case ClientNetworkCalls.TCPClientsTransform:
                if (bufferLength != (8 + BitConverter.ToInt32(data, 4) * 16)) {
                    Debug.Log("Incorrect Buffer Length");
                    break;
                }

                GameController.instance.AddCommand(data);
                break;
            case ClientNetworkCalls.TCPSetClientName:
                GameController.instance.AddCommand(data);
                break;
            case ClientNetworkCalls.TCPClientMessage:
                GameController.instance.AddCommand(data);
                break;
            case ClientNetworkCalls.TCPClientScore:
                GameController.instance.AddCommand(data);
                break;
            default:
                break;
        }
    }

    private static void ReceiveUDPCallback(IAsyncResult result) {
        Socket socket = (Socket)result.AsyncState;
        int bufferLength = 0;

        if (!ConnectedToServer)
            return;

        try {
            bufferLength = socket.EndReceiveFrom(result, ref UDPEndPoint);
        }
        catch (System.Exception exc) {
            Debug.Log("Exception: " + exc.ToString());
            socket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, 0, ref UDPEndPoint, new AsyncCallback(ReceiveUDPCallback), socket);
            return;
        }

        byte[] data = new byte[bufferLength];
        Array.Copy(receiveBuffer, data, bufferLength);
        socket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, 0, ref UDPEndPoint, new AsyncCallback(ReceiveUDPCallback), socket);

        //First byte indicates the type of data sent
        switch ((ClientNetworkCalls)data[0]) {
            case ClientNetworkCalls.UDPClientsTransform:
                if (bufferLength != (8 + BitConverter.ToInt32(data, 4) * 40)) {
                    Debug.Log("Incorrect Buffer Length");
                    break;
                }

                GameController.instance.AddCommand(data);
                //Debug.Log("Received position " + position.ToString());
                break;
            default:
                break;
        }
    }

    private static void SendNetworkCallback(byte[] sendBuffer) {
        try {
            if ((sendBuffer[0] & 0x10) == 0x10)
                UDPSocket.BeginSendTo(sendBuffer, 0, sendBuffer.Length, 0, UDPEndPoint, new AsyncCallback(SendUDPCallback), UDPSocket); 
            else
                TCPSocket.BeginSend(sendBuffer, 0, sendBuffer.Length, 0, new AsyncCallback(SendTCPCallback), TCPSocket);
        }
        catch (Exception exc) {
            Debug.Log("Exception: " + exc.ToString());
        }
    }

    private static void SendTCPCallback(IAsyncResult result) {
        if (!ConnectedToServer)
            return;

        Socket socket = (Socket)result.AsyncState;
        socket.EndSend(result);
    }

    private static void SendUDPCallback(IAsyncResult result) {
        if (!ConnectedToServer)
            return;

        Socket socket = (Socket)result.AsyncState;
        socket.EndSendTo(result);
    }

    private IEnumerator SendPosition() {
        byte[] sendBuffer = new byte[40];

        bool running = true;
        int maxExtraSend = 10;
        int curExtraSend = maxExtraSend;
        float previousTime = Time.unscaledTime;
        float timeWait = 1f / 20f;
        float deltaTime = 0f;

        while (running) {
            //Find the amount of time that has passed since this function was last called
            float workTime = Time.unscaledTime - previousTime;

            //If the amount of time that has passed is smaller than the time to wait it'll find out how long it needs to wait and stop the program from running for a certain amoount of time
            if (workTime < timeWait)
                yield return new WaitForSeconds(timeWait - workTime);

            //Gets deltaTime by looking at difference of current time and previous time
            deltaTime = Time.unscaledTime - previousTime;
            previousTime = Time.unscaledTime;

            //Wait for start of new frame
            yield return null;

            positionPrev = position;
            position = transform.position;
            velocityPrev = velocity;
            velocity = position - positionPrev;
            acceleration = velocity - velocityPrev;

            if (Vector3.Distance(position, positionPrev) > 0.001f)
                curExtraSend = maxExtraSend;

            if (curExtraSend == 0)
                continue;
            
            --curExtraSend;

            BufferSetup(sendBuffer, clientId, position, velocity, acceleration);

            SendNetworkCallback(sendBuffer);
        }
    }

    public void SetTransform(Vector3 pos, Vector3 vel, Vector3 accel) {
        positionPrev = transform.position;

        position = pos;
        velocity = vel;
        acceleration = accel;

        lastTimePosRecieved = Time.unscaledTime;
    }

    public static void SendMessageToServer(byte id, string message) {
        byte[] sendBuffer = new byte[Math.Min(message.Length + 4, 512)];
        BufferSetup(sendBuffer, id, false, message);
        SendNetworkCallback(sendBuffer);
    }

    public static void SetClientName(byte id, string name) {
        byte[] sendBuffer = new byte[Math.Min(name.Length + 4, 64)];
        BufferSetup(sendBuffer, id, true, name);
        SendNetworkCallback(sendBuffer);
    }

    public static void SetScore(byte id, byte score) {
        byte[] sendBuffer = new byte[4];
        BufferSetup(sendBuffer, id, score);
        SendNetworkCallback(sendBuffer);
    }

    private static void BufferSetup(byte[] buffer, byte id) {
        //Initial connection with ID, UDP
        buffer[0] = (byte)ServerNetworkCalls.UDPClientConnection;
        buffer[1] = id;
    }

    private static void BufferSetup(byte[] buffer, byte id, Vector3 pos, Vector3 vel, Vector3 accel) {
        //Send pos, UDP
        buffer[0] = (byte)ServerNetworkCalls.UDPClientTransform;
        buffer[1] = id;
        BitConverter.GetBytes(pos.x).CopyTo(buffer, 4);
        BitConverter.GetBytes(pos.y).CopyTo(buffer, 8);
        BitConverter.GetBytes(pos.z).CopyTo(buffer, 12);
        BitConverter.GetBytes(vel.x).CopyTo(buffer, 16);
        BitConverter.GetBytes(vel.y).CopyTo(buffer, 20);
        BitConverter.GetBytes(vel.z).CopyTo(buffer, 24);
        BitConverter.GetBytes(accel.x).CopyTo(buffer, 28);
        BitConverter.GetBytes(accel.y).CopyTo(buffer, 32);
        BitConverter.GetBytes(accel.z).CopyTo(buffer, 36);
    }

    private static void BufferSetup(byte[] buffer, byte id, bool isNameChange, string message) {
        //Send message, TCP
        buffer[0] = isNameChange ? (byte)ServerNetworkCalls.TCPSetClientName : (byte)ServerNetworkCalls.TCPClientMessage;
        buffer[1] = id;
        Encoding.ASCII.GetBytes(message, 0, message.Length, buffer, 4);
    }

    private static void BufferSetup(byte[] buffer, byte id, byte score) {
        buffer[0] = (byte)ServerNetworkCalls.TCPClientScore;
        buffer[1] = id;
        buffer[2] = score;
    }
}
