using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Nyx
{
    public class NyxPeer
    {
        public int Id;
        public string Name;
        public TcpClient Tcp;
        public NetworkStream Stream;
        public readonly object SendLock = new object();
        public volatile bool Alive = true;
    }

    public class NyxSession : IDisposable
    {
        public readonly bool IsHost;
        public readonly int WorldSeed;
        public readonly string LocalName;
        public int LocalId;
        public string StatusMessage;
        public volatile bool Connected;

        public readonly ConcurrentQueue<(NyxPeer peer, Packet packet)> Inbox = new ConcurrentQueue<(NyxPeer, Packet)>();
        public readonly ConcurrentQueue<string> Notices = new ConcurrentQueue<string>();

        readonly Dictionary<int, NyxPeer> m_peers = new Dictionary<int, NyxPeer>();
        readonly object m_peersLock = new object();
        TcpListener m_listener;
        NyxPeer m_server;
        Thread m_acceptThread;
        int m_nextId = 1;
        volatile bool m_disposed;

        public NyxSession(bool isHost, int worldSeed, string localName)
        {
            IsHost = isHost;
            WorldSeed = worldSeed;
            LocalName = localName;
        }

        public void StartHost(int port)
        {
            m_listener = new TcpListener(IPAddress.Any, port);
            m_listener.Start();
            LocalId = 0;
            Connected = true;
            m_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Nyx-Accept" };
            m_acceptThread.Start();
            Notices.Enqueue("Nyx: servidor ativo na porta " + port);
        }

        public void StartClient(string address, int port)
        {
            var thread = new Thread(() => ConnectLoop(address, port)) { IsBackground = true, Name = "Nyx-Connect" };
            thread.Start();
        }

        void ConnectLoop(string address, int port)
        {
            try
            {
                var tcp = new TcpClient { NoDelay = true };
                tcp.Connect(address, port);
                var peer = new NyxPeer { Id = 0, Name = "host", Tcp = tcp, Stream = tcp.GetStream() };
                m_server = peer;
                Send(peer, new Packet { Type = PacketType.Hello, Text = LocalName, Seed = WorldSeed });
                ReadLoop(peer);
            }
            catch (Exception e)
            {
                Notices.Enqueue("Nyx: falha ao conectar: " + e.Message);
            }
            Connected = false;
        }

        void AcceptLoop()
        {
            while (!m_disposed)
            {
                try
                {
                    var tcp = m_listener.AcceptTcpClient();
                    tcp.NoDelay = true;
                    var peer = new NyxPeer { Tcp = tcp, Stream = tcp.GetStream(), Id = -1 };
                    new Thread(() => HandshakeAndRead(peer)) { IsBackground = true, Name = "Nyx-Peer" }.Start();
                }
                catch
                {
                    if (m_disposed) return;
                }
            }
        }

        void HandshakeAndRead(NyxPeer peer)
        {
            try
            {
                peer.Tcp.ReceiveTimeout = 5000;
                if (!PacketIO.TryRead(peer.Stream, out var hello) || hello.Type != PacketType.Hello)
                {
                    peer.Tcp.Close();
                    return;
                }
                peer.Tcp.ReceiveTimeout = 0;
                if (hello.Id != PacketIO.ProtocolVersion)
                {
                    Send(peer, new Packet { Type = PacketType.Reject, Text = "Versão de protocolo incompatível." });
                    peer.Tcp.Close();
                    return;
                }
                if (hello.Seed != WorldSeed)
                {
                    Send(peer, new Packet { Type = PacketType.Reject, Text = "A semente do mundo é diferente da do host." });
                    peer.Tcp.Close();
                    return;
                }
                lock (m_peersLock)
                {
                    peer.Id = m_nextId++;
                    peer.Name = string.IsNullOrWhiteSpace(hello.Text) ? "Jogador" + peer.Id : hello.Text;
                    Send(peer, new Packet { Type = PacketType.Welcome, Id = peer.Id, Seed = WorldSeed });
                    Send(peer, new Packet { Type = PacketType.PlayerJoin, Id = 0, Text = LocalName });
                    foreach (var other in m_peers.Values)
                    {
                        Send(peer, new Packet { Type = PacketType.PlayerJoin, Id = other.Id, Text = other.Name });
                        Send(other, new Packet { Type = PacketType.PlayerJoin, Id = peer.Id, Text = peer.Name });
                    }
                    m_peers[peer.Id] = peer;
                }
                Inbox.Enqueue((peer, new Packet { Type = PacketType.PlayerJoin, Id = peer.Id, Text = peer.Name }));
                ReadLoop(peer);
            }
            catch
            {
            }
            finally
            {
                RemovePeer(peer);
            }
        }

        void ReadLoop(NyxPeer peer)
        {
            try
            {
                while (!m_disposed && peer.Alive && PacketIO.TryRead(peer.Stream, out var packet))
                {
                    Inbox.Enqueue((peer, packet));
                }
            }
            catch
            {
            }
            peer.Alive = false;
            if (!IsHost)
            {
                Connected = false;
                Notices.Enqueue("Nyx: desconectado do servidor.");
            }
        }

        void RemovePeer(NyxPeer peer)
        {
            peer.Alive = false;
            try { peer.Tcp.Close(); } catch { }
            bool wasRegistered;
            lock (m_peersLock)
            {
                wasRegistered = peer.Id > 0 && m_peers.Remove(peer.Id);
            }
            if (wasRegistered)
            {
                Inbox.Enqueue((peer, new Packet { Type = PacketType.PlayerLeave, Id = peer.Id }));
                Broadcast(new Packet { Type = PacketType.PlayerLeave, Id = peer.Id }, peer.Id);
            }
        }

        public void Send(NyxPeer peer, Packet packet)
        {
            if (peer == null || !peer.Alive) return;
            try
            {
                lock (peer.SendLock)
                {
                    PacketIO.Write(peer.Stream, packet);
                }
            }
            catch
            {
                peer.Alive = false;
            }
        }

        public void SendToServer(Packet packet)
        {
            Send(m_server, packet);
        }

        public void Broadcast(Packet packet, int exceptId = -1)
        {
            List<NyxPeer> targets;
            lock (m_peersLock)
            {
                targets = new List<NyxPeer>(m_peers.Values);
            }
            foreach (var peer in targets)
            {
                if (peer.Id != exceptId) Send(peer, packet);
            }
        }

        public void Dispose()
        {
            m_disposed = true;
            try { m_listener?.Stop(); } catch { }
            lock (m_peersLock)
            {
                foreach (var peer in m_peers.Values)
                {
                    peer.Alive = false;
                    try { peer.Tcp.Close(); } catch { }
                }
                m_peers.Clear();
            }
            if (m_server != null)
            {
                m_server.Alive = false;
                try { m_server.Tcp.Close(); } catch { }
            }
            Connected = false;
        }
    }
}
