using System;
using Dalamud.Game;

namespace AutoHook.SeFunctions
{
    public class SeAddressBase
    {
        public readonly IntPtr Address;

        public bool IsAvailable => Address != IntPtr.Zero;

        public SeAddressBase(ISigScanner sigScanner, string signature, int offset = 0)
        {
            // 特徵碼對不上時 Address 留 0、不擲例外；讀取端一律先判 IsAvailable。
            if (sigScanner.TryGetStaticAddressFromSig(signature, out var address) && address != IntPtr.Zero)
                Address = address + offset;
            var baseOffset = (ulong)Address.ToInt64() - (ulong)sigScanner.Module.BaseAddress.ToInt64();
        }
    }
}
