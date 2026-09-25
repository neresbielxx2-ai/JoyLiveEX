using VirtualJoyCon.Configuration;
using VirtualJoyCon.ControllerModel;

namespace VirtualJoyCon.InputEngine;

public sealed record GamepadInfo(uint Index, string Name, bool Connected, byte Type, byte SubType);

/// <summary>
/// Polls up to 4 XInput slots, auto-detects connect/disconnect, and converts
/// raw gamepad state into the canonical ControllerState using the user's
/// button map + stick tuning. Fully optional: nothing in the app depends on it.
/// </summary>
public sealed class GamepadSource
{
    private readonly Dictionary<string, int> _map;
    private readonly StickTuning _leftTuning;
    private readonly StickTuning _rightTuning;
    private readonly StickCurve _left, _right;
    private uint? _preferred;
    private readonly GamepadInfo[] _slots = new GamepadInfo[4];

    public event Action? GamepadsChanged;
    /// <summary>Last raw button bitmask of the active pad (for remap capture).</summary>
    public volatile ushort RawButtonsLast;
    public int? ActiveSlotIndex => _preferred.HasValue ? (int)_preferred.Value : (int?)_slots.FirstOrDefault(s => s.Connected)?.Index;
    public IReadOnlyList<GamepadInfo> Slots => _slots;
    public uint? ActiveIndex => _preferred;
    public bool AnyConnected => _slots.Any(s => s.Connected);

    public GamepadSource(Dictionary<string, int> buttonMap, StickTuning left, StickTuning right)
    {
        _map = buttonMap;
        _leftTuning = left;
        _rightTuning = right;
        _left = left.ToCurve();
        _right = right.ToCurve();
        for (int i = 0; i < 4; i++)
            _slots[i] = new GamepadInfo((uint)i, $"Slot {i + 1}", false, 0, 0);
    }

    public void SetPreferred(string nameOrIndex)
    {
        if (uint.TryParse(nameOrIndex, out var idx) && idx < 4) { _preferred = idx; return; }
        _preferred = _slots.FirstOrDefault(s => s.Connected && s.Name.Contains(nameOrIndex, StringComparison.OrdinalIgnoreCase))?.Index;
    }

    /// <summary>Called from the hub tick. Fills `target` with this source's contribution.</summary>
    public void Poll(ControllerState target)
    {
        bool anyChange = false;
        uint slotCount = XInputNative.Available ? 4u : 0u;
        for (uint i = 0; i < slotCount; i++)
        {
            bool connected = XInputNative.GetState(i, out var st);
            if (connected != _slots[i].Connected)
            {
                if (connected && XInputNative.GetCapabilities(i, out var caps))
                    _slots[i] = new GamepadInfo(i, $"{XInputNative.DescribeType(caps.Type)} (slot {i + 1})", true, caps.Type, caps.SubType);
                else
                    _slots[i] = _slots[i] with { Connected = connected };
                anyChange = true;
            }
        }
        if (anyChange) GamepadsChanged?.Invoke();

        uint active = _preferred ?? _slots.FirstOrDefault(s => s.Connected)?.Index ?? uint.MaxValue;
        if (active == uint.MaxValue || !XInputNative.GetState(active, out var state)) return;

        var g = state.Gamepad;
        uint buttons = 0;
        RawButtonsLast = g.wButtons;

        // digital mappings via the user map (action -> XInput bit)
        foreach (var (action, mask) in _map)
        {
            if (mask >= 0 && (g.wButtons & (ushort)mask) != 0)
                buttons |= MapActionToButton(action);
        }
        // trigger thresholds as ZL/ZR when mapped to -1/-2
        if (GetMask("ZL") == -1 && g.bLeftTrigger > 127) buttons |= (uint)ButtonFlags.ZL;
        if (GetMask("ZR") == -2 && g.bRightTrigger > 127) buttons |= (uint)ButtonFlags.ZR;

        target.Buttons |= buttons;

        // axes (gamepad Y is up-positive; our convention is Y-down positive like screen space)
        _left.Process(g.sThumbLX / 32768f, -g.sThumbLY / 32768f, out float lx, out float ly);
        _right.Process(g.sThumbRX / 32768f, -g.sThumbRY / 32768f, out float rx, out float ry);
        if (MathF.Abs(lx) > MathF.Abs(target.LeftX)) target.LeftX = lx;
        if (MathF.Abs(ly) > MathF.Abs(target.LeftY)) target.LeftY = ly;
        if (MathF.Abs(rx) > MathF.Abs(target.RightX)) target.RightX = rx;
        if (MathF.Abs(ry) > MathF.Abs(target.RightY)) target.RightY = ry;

        target.LeftTrigger = Math.Max(target.LeftTrigger, g.bLeftTrigger / 255f);
        target.RightTrigger = Math.Max(target.RightTrigger, g.bRightTrigger / 255f);
    }

    private int GetMask(string action) => _map.TryGetValue(action, out var m) ? m : int.MinValue;

    public static uint MapActionToButton(string action) => action switch
    {
        nameof(PadAction.A) => (uint)ButtonFlags.A,
        nameof(PadAction.B) => (uint)ButtonFlags.B,
        nameof(PadAction.X) => (uint)ButtonFlags.X,
        nameof(PadAction.Y) => (uint)ButtonFlags.Y,
        nameof(PadAction.L) => (uint)ButtonFlags.L,
        nameof(PadAction.R) => (uint)ButtonFlags.R,
        nameof(PadAction.ZL) => (uint)ButtonFlags.ZL,
        nameof(PadAction.ZR) => (uint)ButtonFlags.ZR,
        nameof(PadAction.Minus) => (uint)ButtonFlags.Minus,
        nameof(PadAction.Plus) => (uint)ButtonFlags.Plus,
        nameof(PadAction.Home) => (uint)ButtonFlags.Home,
        nameof(PadAction.Capture) => (uint)ButtonFlags.Capture,
        nameof(PadAction.LeftStickClick) => (uint)ButtonFlags.LeftStick,
        nameof(PadAction.RightStickClick) => (uint)ButtonFlags.RightStick,
        nameof(PadAction.Up) => (uint)ButtonFlags.DPadUp,
        nameof(PadAction.Down) => (uint)ButtonFlags.DPadDown,
        nameof(PadAction.Left) => (uint)ButtonFlags.DPadLeft,
        nameof(PadAction.Right) => (uint)ButtonFlags.DPadRight,
        _ => 0u,
    };

    public void Vibrate(ushort left, ushort right, uint? slot = null)
    {
        var idx = slot ?? active0;
        if (idx.HasValue) XInputNative.SetVibration(idx.Value, left, right);
    }
    private uint? active0 => _preferred ?? _slots.FirstOrDefault(s => s.Connected)?.Index;
}
