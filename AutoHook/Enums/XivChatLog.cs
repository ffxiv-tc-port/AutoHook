namespace AutoHook.Enums;

public static class XivChatLog
{
    // 台服 7.20 的 LogMessage row（離線查 exd-tc/7.20/LogMessage.csv 確認）。
    // ⚠️ 這些是 row id 不是 opcode，跟著資料表走；改版後若對不上，表現會是「訊息永遠不命中」
    //    而不是報錯 —— 所以 A2 的階梯 log 一行都沒出現時，第一個要懷疑的就是這裡。
    public const uint
        CantFish = 3516,

        // 「現在感覺能釣到大型／小型獵物！！！」＝ 體型**鎖定**（三驚嘆號）。
        AmbLureSuccess = 5565,
        ModLureSuccess = 5569,

        // 大型（雄心之餌）引誘階梯 1／2／3 層：
        // 「引誘了大型獵物……」「進一步引誘了大型獵物！」「進一步持續引誘了大型獵物！！」
        AmbLureStack1 = 5566,
        AmbLureStack2 = 5567,
        AmbLureStack3 = 5568,

        // 小型（謙遜之餌）引誘階梯 1／2／3 層（文字同上的小型版）。
        ModLureStack1 = 5570,
        ModLureStack2 = 5571,
        ModLureStack3 = 5572;
}