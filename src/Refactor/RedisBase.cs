﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace codecrafters_redis.src
{
    interface IRedisServer
    {
        public void StartServer();
        protected void _HandleClient(Socket Socket) { }
        protected void _SendResponse(string response, Socket socket) { }
        protected string? _DateAsString(double? milliseconds) { return ""; }
        protected void _SyncStorageToRDSFile() { }
        protected IPEndPoint? _GetIPEndPoint() { return null; }

    }

    abstract class RedisServer : IRedisServer
    {
        protected int _listeningPort;
        protected TcpListener _server;
        protected RedisDatabaseStored _storage;
        protected string? _directory;
        protected string? _dbfilename;
        protected RDSFileReader _reader;
        protected string _role;

        public RedisServer(string? dir, string? dbName, int port, string role)
        {
            _listeningPort = port;
            _server = new TcpListener(IPAddress.Any, port);
            _storage = new RedisDatabaseStored();
            _directory = dir;
            _dbfilename = dbName;
            _reader = new RDSFileReader(_directory + "/" + _dbfilename);
            _role = role;
        }


        public virtual void StartServer()
        {
            _server.Start();

            if (_directory + _dbfilename != string.Empty)
            {
                _reader.ReadRDSFile();
                _SyncStorageToRDSFile();
            }

            while (true)
            {
                var socket = _server.AcceptSocket();
                var thread = new Thread(() => _HandleClient(socket));
                thread.Start();
            }
        }

        protected virtual void HandleEchoCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var commandToEcho = command.arguments[1];
            SendResponse(ResponseHandler.BulkResponse(commandToEcho), socket);
        }

        protected virtual void HandleSetCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandleGetCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var keyToRetrieve = command.GetKey();

            StoredValue? retrievedValue = _storage.Get(keyToRetrieve);
            if (retrievedValue != null)
            {
                int valueLength = retrievedValue.Value.Length;

                // Check for Expiry Value
                if (retrievedValue.Expiry != null)
                {
                    var expiryTime = DateTime.Parse(retrievedValue.Expiry);

                    // If Expired
                    if (expiryTime <= DateTime.Now && expiryTime.Millisecond <= DateTime.Now.Millisecond)
                    {

                        _storage.Remove(keyToRetrieve);
                        SendResponse(ResponseHandler.NullResponse(), socket);
                        return;

                    }
                    // If not expired
                    else
                    {
                        SendResponse(ResponseHandler.BulkResponse(retrievedValue.Value), socket);
                        return;
                    }
                }
                //value not set with expiry
                else
                {
                    SendResponse(ResponseHandler.BulkResponse(retrievedValue.Value), socket);
                    return;
                }

            }
            // Key not set
            else
            {
                SendResponse(ResponseHandler.NullResponse(), socket);
                return;
            }
        }

        protected virtual void HandleConfigComamnd(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var configToGet = command.GetConfigParameter();

            if (configToGet == "dir")
            {
                SendResponse(ResponseHandler.ArrayResponse([configToGet, _directory]), socket);
            }
            else if (configToGet == "dbfilename")
            {
                SendResponse(ResponseHandler.ArrayResponse([configToGet, _dbfilename]), socket);
            }
        }

        protected virtual void HandleKeysCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected void HandleInfoComamnd(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var param = command.GetInfoParameter();
            if (param == "replication")
            {
                var bulkString = GenerateBulkStringForInfoComamnd();
                var bulkStringLength = bulkString.Length;

                SendResponse(ResponseHandler.BulkResponse(bulkString), socket);
            }
            else
            {
                Console.Out.WriteLine("INFO command param not supported");
                SendResponse(ResponseHandler.NullResponse(), socket);
            }
        }

        protected abstract string GenerateBulkStringForInfoComamnd();

        protected virtual void HandleReplconfCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandlePsyncCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandlePingCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandleWaitCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected void HandleUnrecognizedComamnd(Socket socket)
        {
            SendResponse(ResponseHandler.NullResponse(), socket);
        }

        protected virtual void _HandleClient(Socket socket)
        {
            while (socket.Connected)
            {
                byte[] buffer = new byte[1024];
                int bytesRead = 0;

                try
                {
                    bytesRead = socket.Receive(buffer);
                }
                catch (SocketException e)
                {

                    Console.Out.WriteLine(e.Message);
                }
                finally
                {
                    if (socket.ReceiveTimeout > 0)
                    {
                        socket.ReceiveTimeout = 0;
                    }
                }

                RedisProtocolParser parser = new RedisProtocolParser(buffer, bytesRead);
                parser.Parse();

                var commandsRecieved = parser.commandArray;

                foreach (var command in commandsRecieved)
                {
                    switch (command.command)
                    {
                        case "ECHO":
                            HandleEchoCommand(command, socket);
                            break;

                        case "SET":
                            HandleSetCommand(command, socket);
                            break;

                        case "GET":
                            HandleGetCommand(command, socket);
                            break;

                        case "CONFIG":
                            HandleConfigComamnd(command, socket);
                            break;

                        case "KEYS":
                            HandleKeysCommand(command, socket);
                            break;

                        case "INFO":
                            HandleInfoComamnd(command, socket);
                            break;

                        case "REPLCONF":
                            HandleReplconfCommand(command, socket);
                            break;

                        case "PSYNC":
                            HandlePsyncCommand(command, socket);
                            break;

                        case "PING":
                            HandlePingCommand(command, socket);  
                            break;

                        case "WAIT":
                            HandleWaitCommand(command, socket);
                            break;

                        default:
                            HandleUnrecognizedComamnd(socket);
                            break;
                    }
                }
            }
        }

        protected void SendResponse(string response, Socket socket)
        {
            byte[] encodedResponse = Encoding.UTF8.GetBytes(response);
            socket.Send(encodedResponse);
        }

        protected string? _DateAsString(double? milliSeconds)
        {
            if (milliSeconds == null)
                return null;
            
            // have to do this to get around the nullable type checking thingy 
            double milliSecondsToParse = milliSeconds.Value;
            return DateTime.Now.AddMilliseconds(milliSecondsToParse).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        protected void _SyncStorageToRDSFile()
        {
            _storage = _reader.GetCurrentDBState();
        }

        protected IPEndPoint _GetIPEndPoint(string host, int port)
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(host);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint ipEndPoint = new(ipAddress, port);
            return ipEndPoint;
        }
    }

    public class StoredValue
    {
        public string Value { get; set; }
        public string? Expiry { get; set; }

        public StoredValue(string value, string? expiry)
        {
            Value = value;
            Expiry = expiry;
        }
    }
}
