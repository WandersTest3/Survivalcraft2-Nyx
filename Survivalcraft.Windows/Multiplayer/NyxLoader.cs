using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;

namespace Nyx
{
    public class RemotePlayer
    {
        public int Id;
        public string Name;
        public Vector3 Position;
        public Vector3 TargetPosition;
        public Quaternion Rotation = Quaternion.Identity;
        public bool HasState;
    }

    public class NyxLoader : ModLoader
    {
        const float SendInterval = 0.066f;

        Project m_project;
        NyxSession m_session;
        NyxConfig m_config;
        SubsystemTerrain m_terrain;
        SubsystemPlayers m_players;
        SubsystemGameInfo m_gameInfo;
        bool m_applyingRemote;
        float m_sendTimer;
        readonly Dictionary<int, RemotePlayer> m_remote = new Dictionary<int, RemotePlayer>();
        readonly PrimitivesRenderer3D m_primitives = new PrimitivesRenderer3D();
        bool m_welcomeHandled;

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("OnProjectLoaded", this);
            ModsManager.RegisterHook("OnProjectDisposed", this);
            ModsManager.RegisterHook("SubsystemUpdate", this);
            ModsManager.RegisterHook("TerrainChangeCell", this);
            ModsManager.RegisterHook("GuiDraw", this);
        }

        public override void OnProjectLoaded(Project project)
        {
            Shutdown();
            m_project = project;
            m_config = NyxConfig.LoadOrCreate();
            string mode = (m_config.Mode ?? "Off").Trim();
            if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) return;

            m_terrain = project.FindSubsystem<SubsystemTerrain>(true);
            m_players = project.FindSubsystem<SubsystemPlayers>(true);
            m_gameInfo = project.FindSubsystem<SubsystemGameInfo>(true);

            string name = string.IsNullOrWhiteSpace(m_config.Name) ? "Jogador" : m_config.Name.Trim();
            int seed = m_gameInfo.WorldSeed;
            bool host = mode.Equals("Host", StringComparison.OrdinalIgnoreCase);
            bool client = mode.Equals("Client", StringComparison.OrdinalIgnoreCase);
            if (!host && !client)
            {
                Log.Warning("Nyx: modo inválido '" + mode + "', use Off, Host ou Client.");
                return;
            }

            m_session = new NyxSession(host, seed, name);
            m_welcomeHandled = host;
            try
            {
                if (host) m_session.StartHost(m_config.Port);
                else m_session.StartClient(m_config.Address, m_config.Port);
            }
            catch (Exception e)
            {
                Log.Warning("Nyx: falha ao iniciar a sessão: " + e.Message);
                m_session.Dispose();
                m_session = null;
            }
        }

        public override void OnProjectDisposed()
        {
            Shutdown();
        }

        void Shutdown()
        {
            m_session?.Dispose();
            m_session = null;
            m_remote.Clear();
            m_project = null;
            m_terrain = null;
            m_players = null;
            m_gameInfo = null;
        }

        void Notify(string text)
        {
            if (m_players == null) return;
            foreach (var player in m_players.ComponentPlayers)
            {
                player.ComponentGui.DisplaySmallMessage(text, Color.White, false, false, 1f);
            }
        }

        public override void SubsystemUpdate(float dt)
        {
            if (m_session == null || m_terrain == null) return;

            while (m_session.Notices.TryDequeue(out var notice)) Notify(notice);

            while (m_session.Inbox.TryDequeue(out var item)) Handle(item.peer, item.packet);

            foreach (var remote in m_remote.Values)
            {
                if (!remote.HasState) continue;
                remote.Position = Vector3.Lerp(remote.Position, remote.TargetPosition, MathUtils.Min(1f, dt * 12f));
            }

            if (!m_session.Connected || !m_welcomeHandled) return;
            m_sendTimer += dt;
            if (m_sendTimer < SendInterval) return;
            m_sendTimer = 0f;

            if (m_players.ComponentPlayers.Count == 0) return;
            var body = m_players.ComponentPlayers[0].ComponentBody;
            var rotation = body.Rotation;
            var packet = new Packet
            {
                Type = PacketType.PlayerState,
                Id = m_session.LocalId,
                X = body.Position.X, Y = body.Position.Y, Z = body.Position.Z,
                Qx = rotation.X, Qy = rotation.Y, Qz = rotation.Z, Qw = rotation.W
            };
            if (m_session.IsHost) m_session.Broadcast(packet);
            else m_session.SendToServer(packet);
        }

        void Handle(NyxPeer peer, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Welcome:
                    m_session.LocalId = packet.Id;
                    m_session.Connected = true;
                    m_welcomeHandled = true;
                    Notify("Nyx: conectado ao servidor.");
                    break;
                case PacketType.Reject:
                    Notify("Nyx: conexão recusada. " + packet.Text);
                    m_session.Connected = false;
                    break;
                case PacketType.PlayerJoin:
                    if (packet.Id != m_session.LocalId)
                    {
                        GetRemote(packet.Id).Name = packet.Text;
                        Notify(packet.Text + " entrou no jogo.");
                    }
                    break;
                case PacketType.PlayerLeave:
                    if (m_remote.TryGetValue(packet.Id, out var left))
                    {
                        Notify(left.Name + " saiu do jogo.");
                        m_remote.Remove(packet.Id);
                    }
                    break;
                case PacketType.PlayerState:
                    {
                        int id = m_session.IsHost && peer != null ? peer.Id : packet.Id;
                        if (id == m_session.LocalId) break;
                        var remote = GetRemote(id);
                        remote.TargetPosition = new Vector3(packet.X, packet.Y, packet.Z);
                        remote.Rotation = new Quaternion(packet.Qx, packet.Qy, packet.Qz, packet.Qw);
                        if (!remote.HasState)
                        {
                            remote.Position = remote.TargetPosition;
                            remote.HasState = true;
                        }
                        if (m_session.IsHost)
                        {
                            packet.Id = id;
                            m_session.Broadcast(packet, id);
                        }
                        break;
                    }
                case PacketType.BlockChange:
                    ApplyRemoteBlock(packet);
                    if (m_session.IsHost && peer != null) m_session.Broadcast(packet, peer.Id);
                    break;
            }
        }

        RemotePlayer GetRemote(int id)
        {
            if (!m_remote.TryGetValue(id, out var remote))
            {
                remote = new RemotePlayer { Id = id, Name = "Jogador" + id };
                m_remote[id] = remote;
            }
            return remote;
        }

        void ApplyRemoteBlock(Packet packet)
        {
            var terrain = m_terrain.Terrain;
            if (!terrain.IsCellValid(packet.Cx, packet.Cy, packet.Cz)) return;
            var chunk = terrain.GetChunkAtCell(packet.Cx, packet.Cz);
            if (chunk == null || chunk.State < TerrainChunkState.InvalidLight) return;
            if (terrain.GetCellValueFast(packet.Cx, packet.Cy, packet.Cz) == packet.Value) return;
            m_applyingRemote = true;
            try
            {
                m_terrain.ChangeCell(packet.Cx, packet.Cy, packet.Cz, packet.Value, true, null);
            }
            finally
            {
                m_applyingRemote = false;
            }
        }

        public override void TerrainChangeCell(SubsystemTerrain subsystemTerrain, int x, int y, int z, int value, out bool Skip)
        {
            Skip = false;
            if (m_session == null || m_applyingRemote || !m_session.Connected || !m_welcomeHandled) return;
            var packet = new Packet { Type = PacketType.BlockChange, Cx = x, Cy = y, Cz = z, Value = value };
            if (m_session.IsHost) m_session.Broadcast(packet);
            else m_session.SendToServer(packet);
        }

        public override void GuiDraw(ComponentGui componentGui, Camera camera, int drawOrder)
        {
            if (m_session == null || m_remote.Count == 0) return;
            if (drawOrder != componentGui.DrawOrders[0]) return;

            var flat = m_primitives.FlatBatch(0, DepthStencilState.Default, RasterizerState.CullNone, BlendState.AlphaBlend);
            var font = m_primitives.FontBatch(ContentManager.Get<BitmapFont>("Fonts/Pericles"), 1, DepthStencilState.None, RasterizerState.CullNone, BlendState.AlphaBlend, SamplerState.LinearClamp);
            var color = new Color(87, 25, 127);
            foreach (var remote in m_remote.Values)
            {
                if (!remote.HasState) continue;
                var min = remote.Position + new Vector3(-0.3f, 0f, -0.3f);
                var max = remote.Position + new Vector3(0.3f, 1.8f, 0.3f);
                flat.QueueBoundingBox(new BoundingBox(min, max), color);
                var tag = remote.Position + new Vector3(0f, 2.1f, 0f);
                float scale = 0.01f * MathUtils.Max(1f, Vector3.Distance(camera.ViewPosition, tag) * 0.35f);
                font.QueueText(remote.Name, tag, camera.ViewRight * scale, -camera.ViewUp * scale, Color.White, TextAnchor.HorizontalCenter | TextAnchor.Bottom);
            }
            m_primitives.Flush(camera.ViewProjectionMatrix, true, int.MaxValue);
        }
    }
}
