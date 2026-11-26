using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>
/// 引誘停手與華麗提鉤條件的純判斷（不讀遊戲），離線探針直接連結這個檔。
/// </summary>
internal static class HookDecisionRules
{
    /// <summary>
    /// 這則引誘訊息算不算「目的達成、不再疊引誘」。語意照上游 <c>MatchesLureSuccess</c>：
    /// Any＝鎖定或魚影、Special＝只認魚影、NotSpecial＝只認鎖定（魚影出現照樣疊）。
    /// 列舉外的值照 Special 處理（維持舊版對未知值的行為）。
    /// </summary>
    internal static bool MatchesLureSuccess(LureTarget target, bool isGenericLock, bool isSpecialShadow) => target switch
    {
        LureTarget.Any => isGenericLock || isSpecialShadow,
        LureTarget.NotSpecial => isGenericLock,
        _ => isSpecialShadow,
    };

    /// <summary>
    /// 華麗提鉤要不要過「這一咬的條件」。一般提鉤有開＝只看一般提鉤那格（既有行為）；
    /// 一般提鉤沒開＝華麗那格自己的條件，且有開的三重／雙重至少一格條件成立 ——
    /// 否則 preset 本來會放掉的咬鉤，只有華麗提鉤會去勾。三重／雙重都沒開時只看華麗那格。
    /// </summary>
    internal static bool StellarConditionPasses(bool normalOn, bool normalPass, bool stellarOwnPass,
        bool tripleOn, bool triplePass, bool doubleOn, bool doublePass)
    {
        if (normalOn)
            return normalPass;

        if (!stellarOwnPass)
            return false;

        if (!tripleOn && !doubleOn)
            return true;

        return (tripleOn && triplePass) || (doubleOn && doublePass);
    }
}
