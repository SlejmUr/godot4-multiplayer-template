using Godot;
using System.Collections.Generic;
using MessagePack;
using NetMessage;
using ImGuiNET;

/*
    Main player script, send movement packets to the server, does CSP, and reconciliation. 
*/
public partial class ClientPlayer : CharacterBody3D
{
    private readonly List<NetMessage.UserInput> _userInputs = new();

    private int _seqStamp = 0;
    private int _networkId = -1;

    public override void _Ready()
    {
        _networkId = Multiplayer.GetUniqueId();
    }

    public override void _PhysicsProcess(double delta)
    {
        var userInput = GenerateUserInput();
        _userInputs.Add(userInput);
        SendInputs();
        MoveLocally(userInput);
        _seqStamp++;
    }

    public override void _Process(double delta)
    {
        DisplayDebugInformation();
    }

    public void ReceiveState(NetMessage.UserState state)
    {
        _userInputs.RemoveAll(input => input.Stamp <= state.Stamp);

        Transform3D expectedTransform = this.GlobalTransform;
        expectedTransform.Origin = state.Position;

        Vector3 expectedVelocity = state.Velocity;

        foreach (var userInput in _userInputs)
        {
            expectedVelocity = PlayerMovement.ComputeMotion(
                this.GetRid(),
                expectedTransform,
                expectedVelocity,
                PlayerMovement.InputToDirection(userInput.Keys));

            expectedTransform.Origin += expectedVelocity * (float)PlayerMovement.FRAME_DELTA;
        }

        var deviation = expectedTransform.Origin - Position;

        if (deviation.Length() > 0.01f)
        {
            // Reconciliation with authoritative state
            this.GlobalTransform = expectedTransform;
            this.Velocity = expectedVelocity;

            GD.PrintErr($"Client {this.Multiplayer.GetUniqueId()} prediction mismatch!");
        }
    }

    private void SendInputs()
    {
        var userCmd = new NetMessage.UserCommand
        {
            Id = Multiplayer.GetUniqueId(),
            Commands = _userInputs.ToArray()
        };

        if (this.IsMultiplayerAuthority() && Multiplayer.GetUniqueId() != 1)
        {
            byte[] data = MessagePackSerializer.Serialize<NetMessage.ICommand>(userCmd);

            (Multiplayer as SceneMultiplayer).SendBytes(data, 1,
                MultiplayerPeer.TransferModeEnum.Unreliable, 0);
        }
    }

    private void MoveLocally(NetMessage.UserInput userInput)
    {
        this.Velocity = PlayerMovement.ComputeMotion(
            this.GetRid(),
            this.GlobalTransform,
            this.Velocity,
            PlayerMovement.InputToDirection(userInput.Keys));

        Position += this.Velocity * (float)PlayerMovement.FRAME_DELTA;
    }

    private NetMessage.UserInput GenerateUserInput()
    {
        byte keys = 0;

        if (Input.IsActionPressed("right")) keys |= (byte)InputFlags.Right;
        if (Input.IsActionPressed("left")) keys |= (byte)InputFlags.Left;
        if (Input.IsActionPressed("forward")) keys |= (byte)InputFlags.Forward;
        if (Input.IsActionPressed("backward")) keys |= (byte)InputFlags.Backward;
        if (Input.IsActionPressed("space")) keys |= (byte)InputFlags.Space;
        if (Input.IsActionPressed("shift")) keys |= (byte)InputFlags.Shift;

        var userInput = new NetMessage.UserInput
        {
            Stamp = _seqStamp,
            Keys = keys
        };

        return userInput;
    }

    private void DisplayDebugInformation()
    {
        ImGui.Begin("Player Network Information");
        ImGui.Text($"Network Id {_networkId}");
        ImGui.Text($"Position {Position.Snapped(Vector3.One * 0.01f)}");
        ImGui.Text($"Redundant Inputs {_userInputs.Count}");
        ImGui.End();
    }
}
