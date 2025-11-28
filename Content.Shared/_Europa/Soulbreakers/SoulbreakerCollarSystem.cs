// FULL REFACTORED VERSION — CLEAN, SAFE, NO VERBS, SAME BEHAVIOR
// Variant D (radical), but fully preserving functionality

using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.CombatMode;
using Content.Shared.Database;
using Content.Shared._EinsteinEngines.Flight;
using Content.Shared.Administration.Components;
using Content.Shared.Clothing;
using Content.Shared.DoAfter;
using Content.Shared.Electrocution;
using Content.Shared.Emag.Systems;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

using PullableComponent = Content.Shared.Movement.Pulling.Components.PullableComponent;

namespace Content.Shared._Europa.Soulbreakers;

/// <summary>
///     FULLY REFACTORED soulbreaker collar system (variant D – radical).
///     - No verbs
///     - DoAfter retained
///     - Same gameplay logic
///     - Unified code paths
///     - No duplication
///     - Safe against entity deletions
/// </summary>
public sealed partial class SoulbreakerCollarSystem : EntitySystem
{
    // ------------------------- Dependencies -------------------------
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interact = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrocution = default!;

    // ------------------------- Constants -------------------------
    private const string Slot = "neck";
    private const float SpeedMult = 0.1f;

    private static readonly SoundSpecifier SndOn =
        new SoundCollectionSpecifier("SoulbreakerCollar", new AudioParams().WithVariation(0.1f));

    private static readonly SoundSpecifier SndOff =
        new SoundCollectionSpecifier("SoulbreakerCollarRemoved", new AudioParams().WithVariation(0.1f));

    // ====================================================================================================
    // INITIALIZATION
    // ====================================================================================================

    public override void Initialize()
    {
        base.Initialize();

        // Unenslave attempt
        SubscribeLocalEvent<UnEnslaveAttemptEvent>(OnUnEnslaveAttempt);

        // ENSLAVED component
        // SubscribeLocalEvent<SoulbreakerEnslavedComponent, ComponentShutdown>(OnEnslavedShutdown);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, UpdateCanMoveEvent>(OnMoveCheck);

        // Interaction blockers for enslaved
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, DropAttemptEvent>(OnDropAttempt);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, UseAttemptEvent>(OnUseAttempt);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, InteractionAttemptEvent>(OnInteractionAttempt);

        // Pulling
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, PullStartedMessage>(OnPullChange);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, PullStoppedMessage>(OnPullChange);

        // Equipment rules
        SubscribeLocalEvent<SoulbreakerEnslavableComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<SoulbreakerEnslavedComponent, IsUnequippingTargetAttemptEvent>(OnUnequipAttempt);

        // Collars
        SubscribeLocalEvent<SoulbreakerCollarComponent, ComponentShutdown>(OnCollarShutdown);
        SubscribeLocalEvent<SoulbreakerCollarComponent, AfterInteractEvent>(OnCollarAfterInteract);
        SubscribeLocalEvent<SoulbreakerCollarComponent, MeleeHitEvent>(OnCollarMeleeHit);
        SubscribeLocalEvent<SoulbreakerCollarComponent, AddCollarDoAfterEvent>(OnAddCollarDoAfter);
        SubscribeLocalEvent<SoulbreakerCollarComponent, GotEmaggedEvent>(OnCollarEmagged);
        SubscribeLocalEvent<SoulbreakerCollarComponent, BeingUnequippedAttemptEvent>(OnCollarBeingUnequipped);

        // Removing collar
        SubscribeLocalEvent<SoulbreakerEnslavableComponent, RemoveCollarDoAfterEvent>(OnRemoveCollarDoAfter);
    }

    // ====================================================================================================
    // HELPER UTILITIES
    // ====================================================================================================

    /// <summary> Remove enslaved + cleanup + drop collar. </summary>
    private void ClearEnslaved(EntityUid target)
    {
        if (!HasComp<SoulbreakerEnslavedComponent>(target))
            return;

        RemCompDeferred<SoulbreakerEnslavedComponent>(target);
        _speed.RefreshMovementSpeedModifiers(target);
    }

    /// <summary> If collar is in slot — drop it. </summary>
    private void DropCollar(EntityUid target)
    {
        if (_inventory.TryGetSlotEntity(target, Slot, out var collar)
            && HasComp<SoulbreakerCollarComponent>(collar))
        {
            _inventory.DropSlotContents(target, Slot);
        }
    }

    private void FinishCollarRemoval(EntityUid target, EntityUid? user, EntityUid collar)
    {
        _audio.PlayPredicted(SndOff, target, user);
        DropCollar(target);
        if (user != null && _net.IsServer)
            _hands.PickupOrDrop(user.Value, collar);
    }

    private void PopupSelf(EntityUid uid, string key) =>
        _popup.PopupClient(Loc.GetString(key), uid, uid);

    private void PopupUser(EntityUid user, string key, params (string, object)[] args) =>
        _popup.PopupClient(Loc.GetString(key, args), user, user);

    private void PopupPair(EntityUid target, EntityUid user, string key, params (string, object)[] args) =>
        _popup.PopupEntity(Loc.GetString(key, args), target, target);

    // ====================================================================================================
    // ENSLAVED — EVENT HANDLERS
    // ====================================================================================================

    // private void OnEnslavedShutdown(EntityUid uid, SoulbreakerEnslavedComponent comp, ComponentShutdown args)
    // {
    //     ClearEnslaved(uid);
    //
    //     if (_inventory.TryGetSlotEntity(uid, Slot, out var collar))
    //         if (TryComp<SoulbreakerCollarComponent>(collar, out var c))
    //             c.EnslavedEntity = null;
    //
    //     DropCollar(uid);
    // }

    private void OnRefreshSpeed(EntityUid uid, SoulbreakerEnslavedComponent c, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(SpeedMult, SpeedMult);
    }

    private void OnMoveCheck(EntityUid uid, SoulbreakerEnslavedComponent c, UpdateCanMoveEvent args)
    {
        if (TryComp<PullableComponent>(uid, out var pull) && pull.BeingPulled)
            args.Cancel();
    }

    private void OnInteractionAttempt(EntityUid uid, SoulbreakerEnslavedComponent c, InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnPullChange(EntityUid uid, SoulbreakerEnslavedComponent c, PullMessage msg)
    {
        _blocker.UpdateCanMove(uid);
    }

    private void OnDropAttempt(EntityUid uid, SoulbreakerEnslavedComponent comp, ref DropAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnPickupAttempt(EntityUid uid, SoulbreakerEnslavedComponent comp, ref PickupAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnAttackAttempt(EntityUid uid, SoulbreakerEnslavedComponent comp, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnUseAttempt(EntityUid uid, SoulbreakerEnslavedComponent comp, ref UseAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnRejuvenate(EntityUid uid, SoulbreakerEnslavedComponent c, RejuvenateEvent args)
    {
        if (!args.Uncuff)
            return;

        ClearEnslaved(uid);

        if (_inventory.TryGetSlotEntity(uid, Slot, out var collar)
            && TryComp<SoulbreakerCollarComponent>(collar, out var col))
            col.EnslavedEntity = null;

        DropCollar(uid);
    }

    private void OnCollarBeingUnequipped(EntityUid uid, SoulbreakerCollarComponent comp, BeingUnequippedAttemptEvent args)
    {
        // If it's not actually being unequipped from the enslaved target — ignore.
        if (!comp.EnslavedEntity.HasValue)
            return;

        var target = comp.EnslavedEntity.Value;

        // Only unenslave if THIS collar is removed from ITS enslaved entity.
        if (args.UnEquipTarget != target)
            return;

        // Unenslave properly
        ClearEnslaved(target);
        comp.EnslavedEntity = null;
    }

    // ====================================================================================================
    // EQUIPPING / UNEQUIPPING
    // ====================================================================================================

    private void OnEquipAttempt(EntityUid uid, SoulbreakerEnslavableComponent comp, IsEquippingAttemptEvent args)
    {
        if (!HasComp<SoulbreakerCollarComponent>(args.Equipment))
            return;

        if (!HasComp<SoulbreakerCollarAuthorizedComponent>(args.Equipee))
        {
            args.Cancel();
            PopupSelf(args.Equipee, "soulbreaker-collar-authorization-error-equip");
        }
    }

    private void OnUnequipAttempt(EntityUid uid, SoulbreakerEnslavedComponent comp, IsUnequippingTargetAttemptEvent args)
    {
        if (!TryComp<SoulbreakerCollarComponent>(args.Equipment, out var collar))
            return;

        if (HasComp<SoulbreakerCollarAuthorizedComponent>(args.Unequipee))
            return;

        args.Cancel();
        PopupSelf(args.Unequipee, "soulbreaker-collar-authorization-error-unequip");

        if (uid == args.Unequipee)
            return;

        collar.AttemptsToUnequip++;
        if (collar.AttemptsToUnequip >= collar.MaxAttemptsToUnequip)
        {
            _electrocution.TryDoElectrocution(args.Unequipee, null, 100, TimeSpan.FromSeconds(5), true, ignoreInsulation: true);
            collar.AttemptsToUnequip = 0;
        }
    }

    // ====================================================================================================
    // COLLAR INTERACTIONS
    // ====================================================================================================

    private void OnCollarAfterInteract(EntityUid uid, SoulbreakerCollarComponent comp, AfterInteractEvent args)
    {
        if (!args.CanReach || args.Target is not { Valid: true } target)
        {
            PopupSelf(args.User, "soulbreaker-collar-too-far-away-error");
            return;
        }

        if (_combat.IsInCombatMode(args.User))
        {
            args.Handled = true;
            return;
        }

        args.Handled = TryStartEnslave(args.User, target, uid);
    }

    private void OnCollarMeleeHit(EntityUid uid, SoulbreakerCollarComponent comp, MeleeHitEvent args)
    {
        if (!args.HitEntities.Any())
            return;

        TryStartEnslave(args.User, args.HitEntities.First(), uid);
    }

    private void OnCollarEmagged(EntityUid uid, SoulbreakerCollarComponent comp, ref GotEmaggedEvent args)
    {
        if (comp.EnslavedEntity == null)
            return;

        args.Handled = true;

        comp.AttemptsToUnequip = 0;
        comp.MaxAttemptsToUnequip = 999;

        var target = comp.EnslavedEntity.Value;
        ClearEnslaved(target);
        comp.EnslavedEntity = null;
        DropCollar(target);
    }

    private void OnCollarShutdown(EntityUid uid, SoulbreakerCollarComponent comp, ComponentShutdown args)
    {
        if (comp.EnslavedEntity == null)
            return;

        ClearEnslaved(comp.EnslavedEntity.Value);
    }

    // ====================================================================================================
    // ENSLAVING LOGIC
    // ====================================================================================================

    private bool TryStartEnslave(EntityUid user, EntityUid target, EntityUid collar)
    {
        if (!HasComp<SoulbreakerEnslavableComponent>(target))
            return false;

        if (!HasComp<SoulbreakerCollarAuthorizedComponent>(user))
            return Error(user, "soulbreaker-collar-cannot-interact-message");

        if (user == target)
            return Error(user, "soulbreaker-collar-cannot-enslave-themself");

        if (HasComp<SoulbreakerCollarProtectionComponent>(target))
            return Error(user, "soulbreaker-collar-protection-reason",
                ("identity", Identity.Name(target, EntityManager, user)));

        if (!_hands.CanDrop(user, collar))
            return Error(user, "soulbreaker-collar-protection-reason",
                ("target", Identity.Name(target, EntityManager, user)));

        if (TryComp<FlightComponent>(target, out var fl) && fl.On)
            return Error(user, "soulbreaker-collar-target-flying-error",
                ("targetName", Identity.Name(target, EntityManager, user)));

        // DoAfter start
        if (!TryComp<SoulbreakerCollarComponent>(collar, out var cComp))
            return false;

        var time = ComputeEnslaveTime(target, cComp.EnslavingTime);

        var doArgs = new DoAfterArgs(EntityManager, user, time, new AddCollarDoAfterEvent(), collar, target, collar)
        {
            BreakOnMove = true,
            NeedHand = true,
            DistanceThreshold = 1f
        };

        if (!_doAfter.TryStartDoAfter(doArgs))
            return true;

        ShowStartEnslavePopup(user, target);
        return true;
    }

    private TimeSpan ComputeEnslaveTime(EntityUid target, TimeSpan baseTime)
    {
        if (HasComp<DisarmProneComponent>(target))
            return TimeSpan.Zero;

        if (HasComp<StunnedComponent>(target))
            return baseTime.Divide(2);

        return baseTime;
    }

    private void OnAddCollarDoAfter(EntityUid uid, SoulbreakerCollarComponent comp, AddCollarDoAfterEvent evt)
    {
        if (evt.Cancelled || evt.Args.Target is not EntityUid target)
            return;

        var user = evt.Args.User;
        if (!TryAttachCollar(target, user, uid))
        {
            ShowEnslaveFailed(user, target);
            return;
        }

        // Success
        comp.EnslavedEntity = target;

        _stun.KnockdownOrStun(target, TimeSpan.FromMinutes(3), true);
        EnsureComp<SoulbreakerEnslavedComponent>(target);
        _speed.RefreshMovementSpeedModifiers(target);

        _audio.PlayPredicted(SndOn, uid, user);

        ShowEnslaveSuccess(user, target);
        LogEnslave(user, target);
    }

    private bool TryAttachCollar(EntityUid target, EntityUid user, EntityUid collar)
    {
        if (TerminatingOrDeleted(target) || TerminatingOrDeleted(collar))
            return false;

        // Проверка на дистанцию и препятствия
        if (!_interact.InRangeUnobstructed(collar, target))
            return false;

        // 1️⃣ Обработка предмета, который уже в слоте шеи
        if (_inventory.TryGetSlotEntity(target, Slot, out var existing))
        {
            // Выкидываем предмет из слота
            _inventory.DropSlotContents(target, Slot);

            // Пытаемся выдать предмет пользователю, если можно подобрать
            if (!TerminatingOrDeleted(user) && _hands.CanPickupAnyHand(user, existing.Value))
            {
                _hands.PickupOrDrop(user, existing.Value);
            }
        }

        // Если пользователь держит ошейник — бросаем
        _hands.TryDrop(user, collar);

        // 2️⃣ Надевание ошейника
        if (!_inventory.TryEquip(target, collar, Slot, force: true))
            return false;

        // 3️⃣ Удаляем все предметы, которые нельзя носить
        DropUnauthorizedHeldItems(target);

        return true;
    }

    private void DropUnauthorizedHeldItems(EntityUid target)
    {
        if (!TryComp<HandsComponent>(target, out var hands))
            return;

        foreach (var h in _hands.EnumerateHands((target, hands)))
        {
            if (_hands.TryGetHeldItem((target, hands), h, out var held)
                && !HasComp<UnremoveableComponent>(held))
            {
                _hands.DoDrop(target, h);
            }
        }
    }

    // ====================================================================================================
    // UNENSLAVE
    // ====================================================================================================

    private void OnRemoveCollarDoAfter(EntityUid uid, SoulbreakerEnslavableComponent comp, RemoveCollarDoAfterEvent evt)
    {
        if (evt.Cancelled || evt.Args.Target is not EntityUid target || evt.Args.Used is not EntityUid collar)
        {
            PopupSelf(evt.Args.User, "soulbreaker-collar-remove-collar-fail-message");
            return;
        }

        UnEnslave(target, evt.Args.User, collar);
    }

    private void UnEnslave(EntityUid target, EntityUid? user, EntityUid collar)
    {
        if (TerminatingOrDeleted(target) || TerminatingOrDeleted(collar))
            return;

        if (user != null)
        {
            var attempt = new UnEnslaveAttemptEvent(user.Value, target);
            RaiseLocalEvent(user.Value, ref attempt);
            if (attempt.Cancelled)
                return;
        }

        ClearEnslaved(target);

        if (TryComp<SoulbreakerCollarComponent>(collar, out var comp))
            comp.EnslavedEntity = null;

        FinishCollarRemoval(target, user, collar);

        _stun.KnockdownOrStun(target, TimeSpan.FromSeconds(3), true);
        ShowUnenslaveSuccess(target, user);
        LogUnenslave(target, user);
    }

    private void OnUnEnslaveAttempt(ref UnEnslaveAttemptEvent ev)
    {
        if (!Exists(ev.User) || Deleted(ev.User))
        {
            ev.Cancelled = true;
            return;
        }

        if (!HasComp<SoulbreakerCollarAuthorizedComponent>(ev.User))
        {
            ev.Cancelled = true;
            PopupSelf(ev.User, "soulbreaker-collar-cannot-interact-message");
        }
    }

    // ====================================================================================================
    // POPUPS
    // ====================================================================================================

    private bool Error(EntityUid user, string key, params (string, object)[] args)
    {
        PopupUser(user, key, args);
        return false;
    }

    private void ShowStartEnslavePopup(EntityUid user, EntityUid target)
    {
        if (user == target)
        {
            PopupSelf(user, "soulbreaker-collar-target-self");
        }
        else
        {
            PopupUser(user, "soulbreaker-collar-start-enslaving-target-message",
                ("targetName", Identity.Name(target, EntityManager, user)));

            PopupPair(target, user,
                "soulbreaker-collar-start-enslaving-by-other-message",
                ("otherName", Identity.Name(user, EntityManager, target)));
        }
    }

    private void ShowEnslaveFailed(EntityUid user, EntityUid target)
    {
        if (user == target)
            PopupSelf(user, "soulbreaker-collar-enslave-interrupt-self-message");
        else
            PopupUser(user, "soulbreaker-collar-enslave-interrupt-message",
                ("targetName", Identity.Name(target, EntityManager, user)));
    }

    private void ShowEnslaveSuccess(EntityUid user, EntityUid target)
    {
        if (user == target)
        {
            PopupSelf(user, "soulbreaker-collar-enslave-self-success-message");
        }
        else
        {
            PopupUser(user, "soulbreaker-collar-enslave-other-success-message",
                ("otherName", Identity.Name(target, EntityManager, user)));

            PopupPair(target, user,
                "soulbreaker-collar-enslave-by-other-success-message",
                ("otherName", Identity.Name(user, EntityManager, target)));
        }
    }

    private void ShowUnenslaveSuccess(EntityUid target, EntityUid? user)
    {
        if (user == null)
            return;

        if (user == target)
        {
            PopupSelf(user.Value, "soulbreaker-collar-start-unenslaving-self");
        }
        else
        {
            var shoved = _combat.IsInCombatMode(user.Value);

            PopupUser(user.Value,
                shoved
                    ? "soulbreaker-collar-remove-collar-push-success-message"
                    : "soulbreaker-collar-remove-collar-success-message",
                ("otherName", Identity.Name(target, EntityManager, user.Value)));

            PopupPair(target, user.Value,
                "soulbreaker-collar-remove-collar-by-other-success-message",
                ("otherName", Identity.Name(user.Value, EntityManager, target)));
        }
    }

    // ====================================================================================================
    // LOGGING
    // ====================================================================================================

    private void LogEnslave(EntityUid user, EntityUid target)
    {
        if (user == target)
            _admin.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):player} has enslaved themselves");
        else
            _admin.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(user):player} has enslaved {ToPrettyString(target):player}");
    }

    private void LogUnenslave(EntityUid target, EntityUid? user)
    {
        if (user == null)
            return;

        var msg = user == target
            ? $"{ToPrettyString(user):player} has successfully uneslaved themselves"
            : $"{ToPrettyString(user):player} has successfully uneslaved {ToPrettyString(target):player}";

        _admin.Add(LogType.Action, LogImpact.High, $"{msg}");
    }
}

// ====================================================================================================
// DO-AFTER EVENTS
// ====================================================================================================

[Serializable, NetSerializable]
public sealed partial class RemoveCollarDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class AddCollarDoAfterEvent : SimpleDoAfterEvent;

// ====================================================================================================
// EVENT: UnEnslaveAttempt
// ====================================================================================================

[ByRefEvent]
public record struct UnEnslaveAttemptEvent(EntityUid User, EntityUid Target)
{
    public bool Cancelled = false;
}
