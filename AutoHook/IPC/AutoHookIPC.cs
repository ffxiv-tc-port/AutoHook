using System;
using AutoHook.Configurations;
using System.Linq;
using ECommons.EzIpcManager;

namespace AutoHook.IPC;

public class AutoHookIPC
{
    private Configuration _cfg = Service.Configuration;

    public AutoHookIPC()
    {
        EzIPC.Init(this, "AutoHook");
    }

    [EzIPC]
    public void SetPluginState(bool state)
    {
        
        _cfg.PluginEnabled = state;
        Service.Save();
    }

    [EzIPC]
    public void SetAutoGigState(bool state)
    {
        _cfg.AutoGigConfig.AutoGigEnabled = state;
        Service.Save();
    }

    [EzIPC]
    public void SetPreset(string preset)
    {
        Service.Save();
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    // 這裡原本漏了 [EzIPC]：夾在 SetPreset 與 CreateAndSelectAnonymousPreset 兩個有屬性的方法中間，
    // 自己卻沒有掛，所以 AutoHook.SetPresetAutogig 從來沒被註冊過。
    // 消費端（ICE 的 IPC/AutoHookIPC.cs）早就宣告了訂閱，帶的又是 SafeWrapper.AnyException，
    // 呼叫下去只會被吞掉、回傳 default —— 完全靜默。
    // 它只做「在既有的魚叉 preset 清單裡挑一個」，沒有任何危險行為，補上屬性即可。
    [EzIPC]
    public void SetPresetAutogig(string preset)
    {
        Service.Save();
        _cfg.AutoGigConfig.SelectedPreset =
            _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"anon_{_import.PresetName}";
        _import.RenamePreset(name);
        Service.Save();
        _cfg.HookPresets.AddNewPreset(_import);
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == name);
        Service.Save();
    }

    [EzIPC]
    public void ImportAndSelectPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"{_import.PresetName}";
        _import.RenamePreset(name);
        
        if (_import is CustomPresetConfig customPreset)
            _cfg.HookPresets.AddNewPreset(customPreset);
        else if (_import is AutoGigConfig gigPreset)
            _cfg.AutoGigConfig.AddNewPreset(gigPreset);

        Service.Save();
    }

    [EzIPC]
    public void DeleteSelectedPreset()
    {
        var selected = _cfg.HookPresets.SelectedPreset;
        if (selected == null) return;
        _cfg.HookPresets.RemovePreset(selected.UniqueId);
        _cfg.HookPresets.SelectedPreset = null;
        Service.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets()
    {
        _cfg.HookPresets.CustomPresets.RemoveAll(p => p.PresetName.StartsWith("anon_"));
        Service.Save();
    }
}