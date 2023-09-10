using Content.Server.Actions;
using Content.Shared.Actions;
using Content.Shared.StatusEffect;
using Content.Shared.Damage;
using Content.Shared.Revenant.Components;
using Content.Server.Guardian;
using Content.Server.Bible.Components;
using Content.Server.Popups;
using Content.Shared.Backmen.Abilities.Psionics;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Backmen.Abilities.Psionics;

public sealed class DispelPowerSystem : EntitySystem
{
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly GuardianSystem _guardianSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DispelPowerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DispelPowerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DispelPowerActionEvent>(OnPowerUsed);

        SubscribeLocalEvent<DispellableComponent, DispelledEvent>(OnDispelled);
        SubscribeLocalEvent<DamageOnDispelComponent, DispelledEvent>(OnDmgDispelled);
        // Upstream stuff we're just gonna handle here
        SubscribeLocalEvent<GuardianComponent, DispelledEvent>(OnGuardianDispelled);
        SubscribeLocalEvent<FamiliarComponent, DispelledEvent>(OnFamiliarDispelled);
        SubscribeLocalEvent<RevenantComponent, DispelledEvent>(OnRevenantDispelled);
    }

    [ValidatePrototypeId<EntityPrototype>] private const string ActionDispel = "ActionDispel";

    private void OnInit(EntityUid uid, DispelPowerComponent component, ComponentInit args)
    {
        _actions.AddAction(uid, ref component.DispelPowerAction, ActionDispel);

        var action = _actions.GetActionData(component.DispelPowerAction);

        if (action?.UseDelay != null)
            _actions.SetCooldown(component.DispelPowerAction, _gameTiming.CurTime, _gameTiming.CurTime + (TimeSpan)  action?.UseDelay!);

        if (TryComp<PsionicComponent>(uid, out var psionic) && psionic.PsionicAbility == null)
            psionic.PsionicAbility = component.DispelPowerAction;
    }

    private void OnShutdown(EntityUid uid, DispelPowerComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, ActionDispel);
    }

    private void OnPowerUsed(DispelPowerActionEvent args)
    {
        if (HasComp<PsionicInsulationComponent>(args.Target))
            return;

        var ev = new DispelledEvent();
        RaiseLocalEvent(args.Target, ev, false);

        if (ev.Handled)
        {
            args.Handled = true;
            _psionics.LogPowerUsed(args.Performer, "dispel");
        }
    }

    private void OnDispelled(EntityUid uid, DispellableComponent component, DispelledEvent args)
    {
        QueueDel(uid);
        Spawn("Ash", Transform(uid).Coordinates);
        _popupSystem.PopupCoordinates(Loc.GetString("psionic-burns-up", ("item", uid)), Transform(uid).Coordinates, Filter.Pvs(uid), true, Shared.Popups.PopupType.MediumCaution);
        _audioSystem.Play("/Audio/Effects/lightburn.ogg", Filter.Pvs(uid), uid, true);
        args.Handled = true;
    }

    private void OnDmgDispelled(EntityUid uid, DamageOnDispelComponent component, DispelledEvent args)
    {
        var damage = component.Damage;
        var modifier = (1 + component.Variance) - (_random.NextFloat(0, component.Variance * 2));

        damage *= modifier;
        DealDispelDamage(uid, damage);
        args.Handled = true;
    }

    private void OnGuardianDispelled(EntityUid uid, GuardianComponent guardian, DispelledEvent args)
    {
        if (TryComp<GuardianHostComponent>(guardian.Host, out var host))
            _guardianSystem.ToggleGuardian(guardian.Host, host);

        DealDispelDamage(uid);
        args.Handled = true;
    }

    private void OnFamiliarDispelled(EntityUid uid, FamiliarComponent component, DispelledEvent args)
    {
        if (component.Source != null)
            EnsureComp<SummonableRespawningComponent>(component.Source.Value);

        args.Handled = true;
    }

    private void OnRevenantDispelled(EntityUid uid, RevenantComponent component, DispelledEvent args)
    {
        DealDispelDamage(uid);
        _statusEffects.TryAddStatusEffect(uid, "Corporeal", TimeSpan.FromSeconds(30), false, "Corporeal");
        args.Handled = true;
    }

    public void DealDispelDamage(EntityUid uid, DamageSpecifier? damage = null)
    {
        if (Deleted(uid))
            return;

        _popupSystem.PopupCoordinates(Loc.GetString("psionic-burn-resist", ("item", uid)), Transform(uid).Coordinates, Filter.Pvs(uid), true, Shared.Popups.PopupType.SmallCaution);
        _audioSystem.Play("/Audio/Effects/lightburn.ogg", Filter.Pvs(uid), uid, true);

        if (damage == null)
        {
            damage = new();
            damage.DamageDict.Add("Blunt", 100);
        }
        _damageableSystem.TryChangeDamage(uid, damage, true, true);
    }
}

public sealed partial class DispelPowerActionEvent : EntityTargetActionEvent {}

public sealed partial class DispelledEvent : HandledEntityEventArgs {}
