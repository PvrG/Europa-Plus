using System.Linq;
using Content.Server.AlertLevel;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._Europa.Copier;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.Fax.Components;
using Content.Shared.NameModifier.Components;
using Content.Shared.Paper;
using Content.Shared.Power;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;

namespace Content.Server._Europa.Copier;


/// <summary>
/// Я понимаю, что это копипаст факса и лютый щиткод. Но оно работает. Можете смело пиздить в свой агпл билд!
/// По вопросам сотрудничества и порта в мит/закрытые репозитории, пишите мне в Discord: 12983218931289 (My username).
/// </summary>
public sealed partial class CopierSystem : EntitySystem
{

    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly IResourceManager _resMan = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedIdCardSystem _cardSystem = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entSotrage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CopierComponent, ComponentInit>(OnCopierInit);

        SubscribeLocalEvent<CopierComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<CopierComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);

        SubscribeLocalEvent<CopierComponent, PowerChangedEvent>(OnPowerChanged);

        SubscribeLocalEvent<CopierComponent, AfterActivatableUIOpenEvent>(OnToggleInterface);

        SubscribeLocalEvent<CopierComponent, CopierPrintMessage>(OnCopierPrint);
        SubscribeLocalEvent<CopierComponent, CopierStopMessage>(OnCopierStop);
        SubscribeLocalEvent<CopierComponent, CopierSelectDocumentMessage>(OnCopierSelectDocumentForm);
        SubscribeLocalEvent<CopierComponent, CopierSelectModeMessage>(OnCopierSelectMode);
        SubscribeLocalEvent<CopierComponent, CopierSelectAmountMessage>(OnCopierSelectDocumentForm);

        SubscribeLocalEvent<CopierComponent, StorageAfterCloseEvent>(OnStorageClose);
    }

    private void OnCopierStop(EntityUid uid, CopierComponent component, CopierStopMessage args)
    {
        if (component.PrintingQueue.Count < 1 || component.PrintingTimeRemaining <= 0)
            return;

        component.PrintingQueue.Clear();
        component.PrintingTimeRemaining = 0;
        component.Paused = false;
        UpdateUserInterface(uid, component);
    }

    private void OnCopierSelectDocumentForm(EntityUid uid, CopierComponent component, CopierSelectAmountMessage args)
    {
        if (component.Amount == args.Amount)
            return;

        component.Amount = args.Amount;
        _adminLogger.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):actor} " +
            $"changed copier amount to {args.Amount}.");
        UpdateUserInterface(uid, component);
    }

    private void OnStorageClose(EntityUid uid, CopierComponent component, ref StorageAfterCloseEvent args)
    {
        if (component.PaperTray.ContainedEntities.Count <= 0)
            return;

        component.Paused = false;
    }

    private void OnToggleInterface(EntityUid uid, CopierComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnCopierSelectDocumentForm(EntityUid uid, CopierComponent component, CopierSelectDocumentMessage args)
    {
        component.SelectedDocument = args.DocumentForm;
        _adminLogger.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):actor} " +
            $"selected copier document form: \"{Loc.GetString(component.SelectedDocument.Name)}\".");
        UpdateUserInterface(uid, component);
    }

    private void OnCopierSelectMode(EntityUid uid, CopierComponent component, CopierSelectModeMessage args)
    {
        component.SelectedMode = args.Mode;
        UpdateUserInterface(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CopierComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var copier, out var receiver))
        {
            if (!receiver.Powered || copier.Paused)
                continue;

            ProcessPrintingAnimation(uid, frameTime, copier);
            ProcessInsertingAnimation(uid, frameTime, copier);
        }
    }

    private void ProcessPrintingAnimation(EntityUid uid, float frameTime, CopierComponent comp)
    {
        if (comp.PrintingTimeRemaining > 0)
        {
            comp.PrintingTimeRemaining -= frameTime;
            UpdateAppearance(uid, comp);

            var isAnimationEnd = comp.PrintingTimeRemaining <= 0;
            if (isAnimationEnd)
            {
                SpawnPaperFromQueue(uid, comp);
                UpdateUserInterface(uid, comp);
            }

            return;
        }

        if (comp.PrintingQueue.Count > 0)
        {
            comp.PrintingTimeRemaining = comp.PrintingTime;
            _audioSystem.PlayPvs(comp.PrintSound, uid);
        }
    }

    private void ProcessInsertingAnimation(EntityUid uid, float frameTime, CopierComponent comp)
    {
        if (comp.InsertingTimeRemaining <= 0)
            return;

        comp.InsertingTimeRemaining -= frameTime;
        UpdateAppearance(uid, comp);

        var isAnimationEnd = comp.InsertingTimeRemaining <= 0;
        if (isAnimationEnd)
            _itemSlots.SetLock(uid, comp.PaperSlot, false);
    }

    private void OnCopierInit(Entity<CopierComponent> entity, ref ComponentInit args)
    {
        // Активируем взлом казино 777
        if (_itemSlots.TryGetSlot(entity.Owner, entity.Comp.PaperSlotId, out var slot))
            entity.Comp.PaperSlot = slot;
        else
            _itemSlots.AddItemSlot(entity.Owner, entity.Comp.PaperSlotId, entity.Comp.PaperSlot);

        entity.Comp.PaperTray = _containerSystem.EnsureContainer<Container>(entity.Owner, entity.Comp.PaperTrayId);
        UpdateAppearance(entity.Owner, entity.Comp);
    }

    private void OnCopierPrint(EntityUid uid, CopierComponent component, CopierPrintMessage args)
    {
        UpdateUserInterface(uid, component);
        switch (component.SelectedMode)
        {
            case CopierMode.Copy:
                if (component.PaperSlot.Item is not { } paperItem)
                    break;

                _audioSystem.PlayPvs(component.CopySound, uid);
                _adminLogger.Add(
                    LogType.Action,
                    LogImpact.Low,
                    $"{ToPrettyString(args.Actor):actor} " +
                    $"started copying {component.Amount} times.");
                CopyDocument(uid, component, args.Actor);
                break;
            case CopierMode.Print:
                if (component.SelectedDocument == null)
                    break;

                _audioSystem.PlayPvs(component.PrintSound, uid);
                _adminLogger.Add(
                    LogType.Action,
                    LogImpact.Low,
                    $"{ToPrettyString(args.Actor):actor} " +
                    $"started printing \"{Loc.GetString(component.SelectedDocument.Name)}\" {component.Amount} times.");
                PrintDocument(uid, component, args.Actor);
                break;
            default:
                Log.Error("Watafak dud! Wat ar u doin??!");
                break;
        }
    }

    private void SpawnPaperFromQueue(EntityUid uid, CopierComponent? component = null)
    {
        if (!Resolve(uid, ref component) || component.PrintingQueue.Count == 0)
            return;

        if (component.PaperTray.ContainedEntities.Count <= 0)
        {
            _entSotrage.OpenStorage(uid);
            UpdateUserInterface(uid, component);
            UpdateAppearance(uid, component);
            component.Paused = true;
            return;
        }

        var printed = component.PaperTray.ContainedEntities.First();
        if (!_containerSystem.RemoveEntity(uid, printed))
            return;

        var printout = component.PrintingQueue.Dequeue();

        if (TryComp<PaperComponent>(printed, out var paper))
        {
            _paper.SetContent((printed, paper), printout.Content);

            if (printout.StampState != null)
            {
                foreach (var stamp in printout.StampedBy)
                {
                    _paper.TryStamp((printed, paper), stamp, printout.StampState);
                }
            }
        }

        _metaData.SetEntityName(printed, printout.Name);

        _adminLogger.Add(LogType.Action, LogImpact.Low, $"\"{Name(uid)}\" {ToPrettyString(uid):tool} printed {ToPrettyString(printed):subject}: {printout.Content}");
    }

    private void PrintDocument(EntityUid uid, CopierComponent component, EntityUid? user)
    {
        var document = _resMan.ContentFileReadAllText(component.SelectedDocument!.Document);

        var formattedContent = FormatDocument(uid, component, document, user);

        var printout = new CopierPrintout(formattedContent,
            Loc.GetString(component.SelectedDocument?.Name ?? "copier-unknown-document-name"),
            component.FallbackPaperId);

        for (var i = 0; i < component.Amount; i++)
        {
            component.PrintingQueue.Enqueue(printout);
            component.PrintingTimeRemaining += component.PrintingTime;
        }

        UpdateUserInterface(uid, component);
    }

    private void CopyDocument(EntityUid uid, CopierComponent component, EntityUid? user)
    {
        var copyingDocument = component.PaperSlot.Item;
        if (copyingDocument == null)
            return;

        if (!TryComp(copyingDocument, out MetaDataComponent? metadata) ||
            !TryComp<PaperComponent>(copyingDocument, out var paper))
            return;

        TryComp<NameModifierComponent>(copyingDocument, out var nameMod);

        var formattedContent = FormatDocument(uid, component, paper.Content, user);

        var printout = new CopierPrintout(formattedContent,
            nameMod?.BaseName ?? metadata.EntityName,
            metadata.EntityPrototype?.ID ?? component.FallbackPaperId,
            paper.StampState,
            paper.StampedBy);

        UpdateUserInterface(uid, component);

        for (var i = 0; i < component.Amount; i++)
        {
            component.PrintingQueue.Enqueue(printout);
            component.PrintingTimeRemaining += component.PrintingTime;
        }
    }

    private string FormatDocument(EntityUid uid, CopierComponent component, string documentContent, EntityUid? user)
    {
        documentContent = documentContent.Replace("%NAME%", Loc.GetString(component.SelectedDocument?.Name ?? "copier-unknown-document-name"));
        documentContent = documentContent.Replace("%TIME%", _ticker.RoundDuration().ToString("hh\\:mm\\:ss"));
        documentContent = documentContent.Replace("%DATE%", DateTime.Now.AddYears(1000).ToString("dd.MM.yyyy")); // I hate this!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

        if (_station.GetOwningStation(uid) is { } station)
        {
            documentContent = documentContent.Replace("%STATION%", Name(station));
            documentContent = documentContent.Replace("%CODE%", "alert-level-" + _alertLevel.GetLevel(station));
        }
        else
        {
            documentContent = documentContent.Replace("%STATION%", Loc.GetString("copier-unknown-station-name"));
            documentContent = documentContent.Replace("%CODE%", Loc.GetString("copier-unknown-station-code"));
        }

        if (user != null && _cardSystem.TryFindIdCard(user.Value, out var idCard))
        {
            documentContent = documentContent.Replace("%FULLNAME%", idCard.Comp.FullName);
            documentContent = documentContent.Replace("%JOB%", idCard.Comp.LocalizedJobTitle);
        }
        else
        {
            documentContent = documentContent.Replace("%FULLNAME%", Loc.GetString("copier-unknown-full-name"));
            documentContent = documentContent.Replace("%JOB%", Loc.GetString("copier-unknown-job"));
        }

        return documentContent;
    }

    private void OnItemSlotChanged(EntityUid uid, CopierComponent component, ContainerModifiedMessage args)
    {
        if (!component.Initialized)
            return;

        if (args.Container.ID != component.PaperSlot.ID)
            return;

        var isPaperInserted = component.PaperSlot.Item.HasValue;
        if (isPaperInserted)
        {
            component.InsertingTimeRemaining = component.InsertionTime;
            _itemSlots.SetLock(uid, component.PaperSlot, true);
        }

        UpdateUserInterface(uid, component);
    }

    private void OnPowerChanged(EntityUid uid, CopierComponent component, ref PowerChangedEvent args)
    {
        var isInsertInterrupted = !args.Powered && component.InsertingTimeRemaining > 0;
        if (isInsertInterrupted)
        {
            component.InsertingTimeRemaining = 0f;

            _itemSlots.SetLock(uid, component.PaperSlot, false);
            _itemSlots.TryEject(uid, component.PaperSlot, null, out var _, true);
        }

        var isPrintInterrupted = !args.Powered && component.PrintingTimeRemaining > 0;
        if (isPrintInterrupted)
        {
            component.PrintingTimeRemaining = 0f;
        }

        if (isInsertInterrupted || isPrintInterrupted)
            UpdateAppearance(uid, component);

        _itemSlots.SetLock(uid, component.PaperSlot, !args.Powered);
    }

    private void UpdateUserInterface(EntityUid uid, CopierComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var isPaperInserted = component.PaperSlot.Item != null;
        var goodTimings = component.PrintingTimeRemaining <= 0 && component.InsertingTimeRemaining <= 0;
        var canPrint = component.PaperTray.ContainedEntities.Count > 0 && goodTimings;
        var canCopy = isPaperInserted && goodTimings;
        var canStop = component.PrintingQueue.Count > 0 && component.PrintingTimeRemaining > 0;

        var state = new CopierUiState(canPrint, canCopy, canStop, component.SelectedDocument, component.SelectedMode, component.Amount);
        _userInterface.SetUiState(uid, CopierUiKey.Key, state);
    }

    private void UpdateAppearance(EntityUid uid, CopierComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (TryComp<FaxableObjectComponent>(component.PaperSlot.Item, out var faxable))
            component.InsertingState = faxable.InsertingState;


        if (component.InsertingTimeRemaining > 0)
        {
            _appearanceSystem.SetData(uid, CopierVisuals.VisualState, CopierVisualState.Inserting);
            Dirty(uid, component);
        }
        else if (component.PrintingTimeRemaining > 0)
            _appearanceSystem.SetData(uid, CopierVisuals.VisualState, CopierVisualState.Printing);
        else
            _appearanceSystem.SetData(uid, CopierVisuals.VisualState, CopierVisualState.Normal);
    }
}
