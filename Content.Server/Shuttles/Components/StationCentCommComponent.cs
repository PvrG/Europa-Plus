// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Maps;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Shuttles.Components;

/// <summary>
/// Spawns Central Command (emergency destination) for a station.
/// </summary>
[RegisterComponent]
public sealed partial class StationCentCommComponent : Component
{
    [DataField(customTypeSerializer:typeof(PrototypeIdSerializer<GameMapPrototype>), required: true)]
    public string Station = default!;

    [DataField]
    public EntityWhitelist? ShuttleWhitelist;

    [DataField]
    public EntityUid StationEntity = EntityUid.Invalid;

    public MapId MapId = MapId.Nullspace;
}
