using Godot;
using System;
using MessagePack;
using System.Linq;
using ImGuiNET;

//
public partial class ServerManager : Node
{
	[Export] private int _port = 9999;

	private SceneMultiplayer _multiplayer = new();
	private Godot.Collections.Array<Godot.Node> entityArray;
	private Godot.Collections.Dictionary<int, Node> Entities;
	private ServerClock _serverClock;

	public override void _EnterTree()
	{
		StartListening();
		_serverClock = GetNode<ServerClock>("ServerClock");
		_serverClock.NetworkProcessTick += NetworkProcess;
		Entities = new();
	}

	public override void _Process(double delta)
	{
		DisplayDebugInformation();
	}

	private void NetworkProcess(double delta)
	{
		if (Entities.Count == 0)
    		return;
		BroadcastSnapshot();
	}

	// Pack and send GameSnapshot with all entities and their information
	private void BroadcastSnapshot()
	{
		var snapshot = new NetMessage.GameSnapshot
		{
			Time = _serverClock.GetCurrentTick(),
			States = new NetMessage.UserState[Entities.Count]
		};
		int state = 0;
		foreach (var item in Entities)
		{
			var player = item.Value as ServerPlayer;
			snapshot.States[state] = player.GetCurrentState();
			state++;
		}

		byte[] data = MessagePackSerializer.Serialize<NetMessage.ICommand>(snapshot);

		_multiplayer.SendBytes(data, 0,
			MultiplayerPeer.TransferModeEnum.Unreliable, 0);
	}

	// Route received Input package to the correspondant Network ID
	private void OnPacketReceived(long id, byte[] data)
	{
		var command = MessagePackSerializer.Deserialize<NetMessage.ICommand>(data);
		if (command is NetMessage.UserCommand userCommand)
		{
			if (Entities.TryGetValue(userCommand.Id, out var node))
			{
				ServerPlayer player = node as ServerPlayer;
				player.PushCommand(userCommand);
			}
			else
			{
				GD.Print("Couldnt get the node from entity!");
			}
		}

	}

	private void OnPeerConnected(long id)
	{
		Node playerInstance = GetNode<MultiplayerSpawner>("/root/Main/MultiplayerSpawner").Spawn(id);
		GD.Print($"Peer {id} connected");
		Entities.Add((int)id, playerInstance);
	}

	private void OnPeerDisconnected(long id)
	{
		GetNode($"/root/Main/EntityArray/{id}").QueueFree();
		GD.Print($"Peer {id} disconnected");
		Entities.Remove((int)id);
	}

	// Starts the server
	private void StartListening()
	{
		_multiplayer.PeerConnected += OnPeerConnected;
		_multiplayer.PeerDisconnected += OnPeerDisconnected;
		_multiplayer.PeerPacket += OnPacketReceived;

		ENetMultiplayerPeer peer = new();
		peer.CreateServer(_port);

		_multiplayer.MultiplayerPeer = peer;
		GetTree().SetMultiplayer(_multiplayer);

		GD.Print("Server listening on ", _port);
	}

	private void DisplayDebugInformation()
	{
		ImGui.Begin($"Server Information");
		ImGui.Text($"Network Tickrate {_serverClock.GetNetworkTickRate()}hz");
		ImGui.Text($"Physics Tickrate {Engine.PhysicsTicksPerSecond}hz");
		ImGui.Text($"Clock {_serverClock.GetCurrentTick()} ticks");
		ImGui.End();
	}
}
