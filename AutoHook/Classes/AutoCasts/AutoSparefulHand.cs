using AutoHook.Conditions;
using AutoHook.Resources.Localization;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public class AutoSparefulHand : BaseActionCast
{
    public AutoSparefulHand(string name, uint id, ActionType actionType = ActionType.Action) : base(name, id,
        actionType)
    {
    }

    public AutoSparefulHand() : base(UIStrings.SparefulHand, Data.IDs.Actions.SparefulHand)
    {
    }

    /// <summary>條件裡 SwimbaitCountCD 沒指定魚時要數的魚；null＝用 <see cref="EvaluatingFishId"/>（剛釣起的那條）。</summary>
    public uint? FishIdToCheck { get; set; }

    /// <summary>呼叫端在判斷前填入剛釣起的魚。不存檔。</summary>
    [Newtonsoft.Json.JsonIgnore] public uint EvaluatingFishId;

    protected override ConditionContext ActionConditionContext()
        => ConditionContext.ForAction(swimbaitFishId: FishIdToCheck ?? EvaluatingFishId);

    public override int Priority { get; set; } = 20;

    public override string GetName()
        => Name = UIStrings.SparefulHand;

    public override bool CastCondition()
    {
        return true;
    }

    public override bool IsExcludedPriority { get; set; } = false;
}