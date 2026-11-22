using AutoHook.Resources.Localization;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public class AutoMooch2 : BaseActionCast
{
    public override int Priority { get; set; } = 11;
    public override bool IsExcludedPriority { get; set; } = true;
    
    public override bool RequiresTimeWindow() => true;

    public AutoMooch2() : base(UIStrings.UseMoochII, Data.IDs.Actions.Mooch2, ActionType.Action)
    {
    }

    public override string GetName()
        => Name = UIStrings.UseMoochII;

    protected override bool HonorsConditionSet => false;

    public override bool CastCondition()
    {
        return true;
    }

    /*protected override DrawOptionsDelegate DrawOptions => () =>
    {

    };*/
}