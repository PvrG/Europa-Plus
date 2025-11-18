// SPDX-FileCopyrightText: 2022 Kevin Zheng <kevinz5000@gmail.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <drsmugleaf@gmail.com>
// SPDX-FileCopyrightText: 2023 keronshb <54602815+keronshb@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading;
using Robust.Shared.Audio; // Europa-Add

namespace Content.Server.Botany
{
    /// <summary>
    /// Anything that can be used to cross-pollinate plants.
    /// </summary>
    [RegisterComponent]
    public sealed partial class BotanySwabComponent : Component
    {
        [DataField("swabDelay")]
        public float SwabDelay = 1f; // Europa-Edit

// Europa-Start

        /// <summary>
        /// Are the swab's contents replaced on swabbing, default true.
        /// </summary>
        [DataField]
        public bool Contaminate = true;

        /// <summary>
        /// Whether the swab is self-cleanable, default false
        /// </summary>
        [DataField]
        public bool Cleanable = false;

        /// <summary>
        /// Whether the swab can be used if it has no seed data, default true
        /// If false, a seperate way to provide seed data is required or the swab will be unusable
        /// </summary>
        [DataField]
        public bool UsableIfClean = true;

        /// <summary>
        /// Sound played on swabbing
        /// </summary>
        [DataField]
        public SoundSpecifier? SwabSound = new SoundPathSpecifier("/Audio/Effects/Footsteps/grass2.ogg", AudioParams.Default.WithVolume(-4f));

        /// <summary>
        /// Sound played on cleaning a swab
        /// </summary>
        [DataField]
        public SoundSpecifier? CleanSound = new SoundPathSpecifier("/Audio/Effects/unwrap.ogg");

// Europa-End

        /// <summary>
        /// SeedData from the first plant that got swabbed.
        /// </summary>
        public SeedData? SeedData;
    }
}
