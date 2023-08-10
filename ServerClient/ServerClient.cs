using System.Text.Json;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Net;
using System.Diagnostics;
using ServerClient;
using System.Numerics;

namespace ServerClient
{
    public class Client
    {
        NetPeerConfiguration config;
        NetClient client;

        string name;
        public int id;

        string ip;
        int port;

        List<string> packetBuffer = new List<string>();

        public Texture2D texture;
        public Point fullScreenSize;
        public Point sizeOffset;
        DateTime lastTickTime = DateTime.Now;

        bool intialised = false;
        bool connected = false;
        int ticks = 0;
        KeyboardState prev = Keyboard.GetState();

        /***********************
         * Checklist:
         * Have client object in game
         * Initalise with ip and port
         * Connect to server if not connected
         * Read messages when connected
         * Send "ready" message (only for one)
         ***********************/

        public Client(string ip, int port)
        {
            this.ip = ip; this.port = port;

            config = new NetPeerConfiguration("Swinging man");
            config.AutoFlushSendQueue = false;
            client = new NetClient(config);
            client.Start();
        }

        public void sendMessage(string message)
        {
            NetOutgoingMessage sendMsg = client.CreateMessage();
            sendMsg.Write(message);
            client.SendMessage(sendMsg, NetDeliveryMethod.ReliableOrdered);
            client.FlushSendQueue();
        }
        public bool connectToServer()
        {

            NetOutgoingMessage hailMsg = client.CreateMessage("Am Client");
            if (ip == "localhost")
            {
                client.Connect(ip, port);
            }
            if (IPAddress.TryParse(ip, out IPAddress ipAddress))
            {
                client.Connect(ip, port, hailMsg);
            }

            NetIncomingMessage incMsg;
            while ((incMsg = client.ReadMessage()) != null)
            {
                switch (incMsg.MessageType)
                {
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus status = (NetConnectionStatus)incMsg.ReadByte();
                        if (status == NetConnectionStatus.Connected)
                        {
                            return true;
                        }
                        else if (status == NetConnectionStatus.Disconnected)
                        {
                            return false;
                        }
                        break;
                }
                client.Recycle(incMsg);
            }

            return false;
        }
        public void tick(bool focus)
        {
            if (connected)
            {
                if (Keyboard.GetState().IsKeyDown(Keys.Enter) && !prev.IsKeyDown(Keys.Enter))
                {
                    sendMessage("ready");
                }

                //OLD CLIENT TICK
                List<string> packets = readMessages();
                if (intialised)
                {
                    string finalPacket = "";
                    for (int i = 0; i < packets.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(packets[i]))
                        {
                            finalPacket = packets[i];
                        }
                    }
                    //sorts and takes the most recent packet: finalPacket
                }

                //checks for disconnecting
                if (ticks++ % 10 == 0)
                {
                    if (DateTime.Now - lastTickTime > TimeSpan.FromMilliseconds(2000))
                    {
                        connected = false;
                        intialised = false;

                        config = new NetPeerConfiguration("Swinging man");
                        config.AutoFlushSendQueue = false;
                        client = new NetClient(config);
                        client.Start();
                    }
                }
            }
            else
            {
                if (ticks++ % 10 == 0)
                {
                    connected = connectToServer();
                }
                if (connected)
                {
                    //sendMessage("ready");
                }
            }
            prev = Keyboard.GetState();
        }

        public void disconnect()
        {
            client.Disconnect("goodbye");
        }

        public void draw(SpriteBatch spriteBatch)
        {
            //TODO: add draw code
        }


        public List<string> readMessages()
        {
            List<string> messages = new List<string>();
            NetIncomingMessage incMsg;
            while ((incMsg = client.ReadMessage()) != null)
            {
                switch (incMsg.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        string message = incMsg.ReadString();

                        if (!string.IsNullOrEmpty(message))
                        {
                            if (message[0] == 'T' && message[1] == 'i')
                            {
                                sendMessage("P" + id.ToString());
                                lastTickTime = DateTime.Now;
                                break;
                            }
                            if (message[0] == 'I')
                            {
                                this.id = int.Parse(message[1..]);
                                break;
                            }
                            if (message[0] == 'S')
                            {
                                //TODO: set up player ids
                                string[] cakeSlices = message.Split(':');

                                int[] playerIDs = new int[cakeSlices.Length - 1];

                                for (int i = 0; i < playerIDs.Length; i++)
                                {
                                    playerIDs[i] = int.Parse(cakeSlices[i + 1]);
                                }

                                intialised = true;
                                break;
                            }
                            messages.Add(message);
                        }
                        break;
                    case NetIncomingMessageType.WarningMessage:
                        Debug.WriteLine(incMsg.ReadString());
                        break;
                    default:
                        Debug.WriteLine("Unhandled type: " + incMsg.MessageType);
                        break;
                }
                client.Recycle(incMsg);
            }

            packetBuffer = messages;
            return messages;
        }
    }

    public class server
    {
        private void initalise()
        {
            //TODO: Initalise world

        }

        public void tickServer()
        {
            //TODO: Implement server tick

        }

        /*
        0: annoucements
        1: generic update
        2: send
        3: recive
        */

        private NetServer Server;
        private List<(NetConnection connection, int playerIds, string name, DateTime lastPing)> connectedPlayers = new List<(NetConnection connection, int playerIds, string name, DateTime lastPing)>();
        private List<(int playerIds, string name, string ip)> disconnectedPlayers = new List<(int playerIds, string name, string ip)>();

        private consoleHelper _consoleHelper = new consoleHelper();

        Random ider = new Random();

        bool lobby = true;

        int playerThreshold;

        bool ready = false;

        int ticks;
        public server(int port, int playerThreshold)
        {
            NetPeerConfiguration config = new NetPeerConfiguration("Swinging man");

            config.Port = port;

            Server = new NetServer(config);
            Server.Start();

            this.playerThreshold = playerThreshold;

            connectedPlayers = new List<(NetConnection connection, int playerIds, string name, DateTime lastPing)>();

            _consoleHelper.add(36, ConsoleColor.DarkRed);
            _consoleHelper.add(22);
            _consoleHelper.add(42, ConsoleColor.Yellow);
            _consoleHelper.add(42, ConsoleColor.Blue);

            _consoleHelper.write(0, $"Server started on port: {port}");
        }

        public void start()
        {
            long targetTicksInterval = Stopwatch.Frequency / 60;
            Stopwatch stopwatch = Stopwatch.StartNew();
            long nextTickTime = stopwatch.ElapsedTicks;
            while (true)
            {
                ticks++;
                checkDisconnections();
                if (lobby)
                {
                    tickLobby();
                }
                else
                {
                    tickServer();

                    if (connectedPlayers.Count < playerThreshold)
                    {
                        lobby = true;
                        _consoleHelper.write(0, "Game Ended Due To Lack of players");
                    }
                }

                nextTickTime += targetTicksInterval;
                int waits = 0;
                while (stopwatch.ElapsedTicks < nextTickTime)
                {
                    waits++;
                    Thread.Sleep(0);
                }
                _consoleHelper.write(2, "Cycles Waited:" + waits.ToString());
            }
        }

        private void checkDisconnections()
        {
            if (ticks % 10 == 0)
            {
                _consoleHelper.write(1, $"Ticks:{ticks} Players: {connectedPlayers.Count}");
                writeMessage($"Ticks:{ticks}  Players: {connectedPlayers.Count}");

                for (int i = 0; i < connectedPlayers.Count; i++)
                {
                    NetConnection playerConnection = connectedPlayers[i].Item1;
                    int playerId = connectedPlayers[i].Item2;
                    string name = connectedPlayers[i].Item3;
                    DateTime lastPing = connectedPlayers[i].Item4;
                    if (DateTime.Now - lastPing > TimeSpan.FromMilliseconds(2000))
                    {
                        _consoleHelper.write(0, $"Player {playerId}, {name} Disconnected   Sent 12 pings unresponded");

                        connectedPlayers.RemoveAll(player => player.connection == playerConnection);
                    }
                }
            }
        }

        public void tickLobby()
        {
            List<string> packets = readPackets();

            foreach (string packet in packets)
            {
                if (packet == "ready")
                {
                    ready = true;
                    string playerInfo = "S";
                    for (int i = 0; i < connectedPlayers.Count; i++)
                    {
                        playerInfo += ":" + connectedPlayers[i].playerIds;
                    }
                    writeMessage(playerInfo);
                }


                string[] parts = packet.Split(':');

                if (parts.Length == 2)
                {
                    for (int i = 0; i < connectedPlayers.Count; i++)
                    {
                        NetConnection playerConnection = connectedPlayers[i].Item1;
                        int playerId = connectedPlayers[i].Item2;
                        string name = connectedPlayers[i].Item3;

                        if (playerId == int.Parse(parts[0]))
                        {
                            name = parts[1];
                            connectedPlayers[i] = (playerConnection, playerId, name, DateTime.Now);
                            break;
                        }
                    }
                }
            }

            if (this.ready && connectedPlayers.Count >= playerThreshold)
            {
                ready = false;
                lobby = false;

                while (connectedPlayers.Count > playerThreshold)
                {
                    (NetConnection connection, int playerIds, string name, DateTime lastPing) playerToRemove = connectedPlayers[connectedPlayers.Count - 1];
                    connectedPlayers.RemoveAt(connectedPlayers.Count - 1);
                    _consoleHelper.write(0, $"Player {playerToRemove.playerIds} removed due to game size");
                }


                int[] playerIDs = new int[connectedPlayers.Count];
                for (int i = 0; i < connectedPlayers.Count; i++)
                {
                    playerIDs[i] = connectedPlayers[i].playerIds;
                }

                _consoleHelper.write(0, $"Game Started At T:{ticks}");

                initalise();
            }
            else
            {
                ready = false;
            }
        }

        private List<string> readPackets()
        {
            NetIncomingMessage msg;
            List<string> packets = new List<string>();

            while ((msg = Server.ReadMessage()) != null)
            {
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.StatusChanged:
                        HandleStatusChange(msg);
                        break;
                    case NetIncomingMessageType.Data:
                        string packet = msg.ReadString();
                        //consoleHelper.write(3, packet);

                        if (packet[0] == 'P')
                        {
                            for (int i = 0; i < connectedPlayers.Count; i++)
                            {
                                NetConnection playerConnection = connectedPlayers[i].Item1;
                                int playerId = connectedPlayers[i].Item2;
                                string name = connectedPlayers[i].Item3;

                                if (playerId == int.Parse(packet[1..]))
                                {
                                    connectedPlayers[i] = (playerConnection, playerId, name, DateTime.Now);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            packets.Add(packet);
                        }
                        break;

                    case NetIncomingMessageType.ConnectionApproval:
                        msg.SenderConnection.Approve();
                        break;
                    default:
                        break;
                }
                Server.Recycle(msg);
            }

            return packets;
        }

        private void HandleStatusChange(NetIncomingMessage msg)
        {
            NetConnectionStatus status = (NetConnectionStatus)msg.ReadByte();
            switch (status)
            {
                case NetConnectionStatus.Connected:
                    if(lobby)
                    {
                        assignID(msg.SenderConnection);
                        _consoleHelper.write(0, $"Player connected: {msg.SenderConnection.RemoteEndPoint}");
                    }
                    else
                    {
                        if (!disconnectedPlayers.Any(disconnectedPlayer => disconnectedPlayer.ip == msg.SenderConnection.RemoteEndPoint.Address.ToString()))
                        {
                            var disconnected = disconnectedPlayers.FirstOrDefault(disconnectedPlayer => disconnectedPlayer.ip == msg.SenderConnection.RemoteEndPoint.Address.ToString());

                            var newPlayer = (msg.SenderConnection, disconnected.playerIds, disconnected.name, DateTime.Now);

                            connectedPlayers.Add(newPlayer);

                            _consoleHelper.write(0, $"Player rejoined current game: {msg.SenderConnection.RemoteEndPoint}");
                        }
                        else
                        {
                            _consoleHelper.write(0, $"Player denied entry due to current game: {msg.SenderConnection.RemoteEndPoint}");
                        }
                    }
                    break;
                case NetConnectionStatus.Disconnected:
                    var player = connectedPlayers.Find(player => player.connection == msg.SenderConnection);

                    var disconnectedPlayer = (player.playerIds, player.name, msg.SenderConnection.RemoteEndPoint.Address.ToString());

                    disconnectedPlayers.Add(disconnectedPlayer);

                    connectedPlayers.Remove(player);

                    _consoleHelper.write(0, $"Player disconnected: {msg.SenderConnection.RemoteEndPoint}");
                    break;
            }
        }

        private void assignID(NetConnection playerConnection)
        {
            NetOutgoingMessage msg = Server.CreateMessage();


            int id = ider.Next();


            _consoleHelper.write(0, $"Assigned ID:{id}");

            msg.Write(string.Format("I{0}", id));

            connectedPlayers.Add((playerConnection, id, "", DateTime.Now));

            Server.SendMessage(msg, playerConnection, NetDeliveryMethod.ReliableOrdered);
        }

        private void writeMessage(string message)
        {
            NetOutgoingMessage msg;
            if (message[0] != 'T')
                //consoleHelper.write(2, message);
                if (message[0] == 'S')
                    //consoleHelper.write(0, message);

                    foreach ((NetConnection playerConnection, int playerId, string name, DateTime lastPing) in connectedPlayers)
                    {
                        msg = Server.CreateMessage();
                        msg.Write(message);
                        Server.SendMessage(msg, playerConnection, NetDeliveryMethod.ReliableOrdered);
                    }
        }

        internal class consoleHelper
        {
            List<row> rows = new List<row>();

            public consoleHelper()
            {
            }

            public void add(int width)
            {
                int prevLines = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    prevLines += rows[i].width + 1;
                }

                rows.Add(new row(width, prevLines, ConsoleColor.White));
            }

            public void add(int width, ConsoleColor color)
            {
                int prevLines = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    prevLines += rows[i].width + 1;
                }

                rows.Add(new row(width, prevLines, color));
            }

            public void write(int row, string text)
            {
                rows[row].write(text);
            }

            internal class row
            {
                int alreadyWritten;
                int startW;
                public int width;
                string clear = "";
                ConsoleColor color;
                public row(int width, int start)
                {
                    this.width = width;
                    this.startW = start;
                    alreadyWritten = 0;

                    for (int i = 0; i < width; i++)
                    {
                        clear += " ";
                    }
                }

                public row(int width, int start, ConsoleColor color)
                {
                    this.width = width;
                    this.startW = start;
                    alreadyWritten = 0;

                    for (int i = 0; i < width; i++)
                    {
                        clear += " ";
                    }
                    this.color = color;
                }
                public void write(string text)
                {
                    Console.ForegroundColor = color;
                    List<string> lines = new List<string>();
                    while (text.Length > width)
                    {
                        lines.Add(text.Substring(0, width));
                        text = text.Substring(width);
                    }
                    lines.Add(text);

                    for (int i = 0; i < lines.Count; i++)
                    {
                        Console.SetCursorPosition(startW, alreadyWritten);
                        Console.Write(lines[i]);
                        alreadyWritten++;
                        alreadyWritten = alreadyWritten % (Console.BufferHeight - 1);
                        Console.SetCursorPosition(startW, alreadyWritten);
                        Console.Write(clear);
                    }
                }
            }
        }
    }

    public static class packetManager
    {
        public static string encodeWID<T>(T givenObject, int playerID)
        {
            string serializedString = JsonSerializer.Serialize(givenObject);
            return playerID.ToString() + ":" + serializedString;
        }

        public static T decodeWID<T>(string packet, out int id)
        {
            string idString = packet.Split(":")[0];
            id = int.Parse(idString);
            return JsonSerializer.Deserialize<T>(packet[(idString.Length + 1)..]);
        }

        public static string encode<T>(T givenObject)
        {
            return JsonSerializer.Serialize(givenObject);
        }

        public static T decode<T>(string packet)
        {
            return JsonSerializer.Deserialize<T>(packet);
        }
    }
}