using Dalamud.Game.Command;
using Dalamud.Logging;
using Dalamud.Plugin;
using Lumina.Excel.GeneratedSheets;
using ImGuiNET;
using ImGuiScene;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Memory;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using Dalamud.Game.Text.SeStringHandling;
using System.Xml.Linq;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using System.Collections;
using System.Security.Cryptography;
using Dalamud.Configuration;

namespace Chameleon
{
    public class Chameleon : IDalamudPlugin
    {
        private DalamudPluginInterface pi;
        private Assembly dalamudAssembly = null;
        public string Name => "Chameleon";

        #region hook define
        private byte[] backup = new byte[64];
        private byte[] target = new byte[64];
        private byte[] inputs = new byte[64];
        public IntPtr UpdatePlayerStatusName { get; private set; }
        //void (GameObject *, char *)
        private delegate IntPtr UpdatePlayerStatusNameDelegate(IntPtr address, IntPtr name);
        private Hook<UpdatePlayerStatusNameDelegate> UpdatePlayerStatusNameHook;
        private unsafe IntPtr UpdatePlayerStatusNameDetour(IntPtr address, IntPtr name)
        {
            var ptr = stackalloc byte[64];
            for (int i = 0; i < 64; i++) ptr[i] = target[i];
            if (address == (nint)PlayerState.Instance())
                return UpdatePlayerStatusNameHook.Original(address, (nint)ptr);
            else
                return UpdatePlayerStatusNameHook.Original(address, name);
        }
        public IntPtr UpdatePlayerCharaName { get; private set; }
        //void (GameObject *, char *)
        private delegate IntPtr UpdatePlayerCharaNameDelegate(IntPtr address, IntPtr name);
        private Hook<UpdatePlayerCharaNameDelegate> UpdatePlayerCharaNameHook;
        private unsafe IntPtr UpdatePlayerCharaNameDetour(IntPtr address, IntPtr name)
        {
            if (DalamudApi.ClientState.LocalPlayer && address == DalamudApi.ClientState.LocalPlayer?.Address && target[0] != 0)
            {
                PluginLog.Log($"Faking: {System.Text.Encoding.UTF8.GetString(target)}");
                var ptr = stackalloc byte[64];
                for (int i = 0; i < 64; i++) ptr[i] = target[i];
                return UpdatePlayerCharaNameHook.Original(address, (nint)ptr);
            }
            else
                return UpdatePlayerCharaNameHook.Original(address, name);
        }
        public IntPtr SpecialName { get; private set; }
        #endregion


        public Chameleon(DalamudPluginInterface pi)
        {
            this.pi = pi;
            pi.UiBuilder.OpenConfigUi += OpenUI;
            pi.UiBuilder.Draw += DrawUI;
            this.dalamudAssembly = pi.GetType().Assembly;
            DalamudApi.Initialize(this, pi);

            SpecialName = DalamudApi.SigScanner.GetStaticAddressFromSig("0f ?? ?? ?? ?? ?? ?? 89 4c 24 ?? 40");
            UpdatePlayerStatusName = DalamudApi.SigScanner.ScanText("48 89 5c 24 ?? 48 89 74 24 ?? 57 48 ?? ?? ?? ?? ?? ?? 48 ?? ?? ?? ?? ?? ?? 48 ?? ?? 48 89 84 24 ?? ?? ?? ?? 48 ?? ?? 48 ?? ?? ?? 0f 1f 44 00");
            PluginLog.Log($"UpdatePlayerStatusName: {UpdatePlayerStatusName:X}");
            UpdatePlayerCharaName = DalamudApi.SigScanner.ScanText("40 ?? 48 ?? ?? ?? 48 ?? ?? 48 ?? ?? 0f 84 ?? ?? ?? ?? 32");
            PluginLog.Log($"UpdatePlayerCharaName: {UpdatePlayerCharaName:X}");


            UpdatePlayerStatusNameHook ??= Hook<UpdatePlayerStatusNameDelegate>.FromAddress(UpdatePlayerStatusName, UpdatePlayerStatusNameDetour);
            UpdatePlayerCharaNameHook ??= Hook<UpdatePlayerCharaNameDelegate>.FromAddress(UpdatePlayerCharaName, UpdatePlayerCharaNameDetour);
            UpdatePlayerStatusNameHook.Enable();
            UpdatePlayerCharaNameHook.Enable();

            Init();
            DalamudApi.ClientState.Login += LoginInit;
        }

        public void LoginInit(object? sender, EventArgs e) => Init();
        public unsafe void Init()
        {
            if (DalamudApi.ClientState.IsLoggedIn)
            {
                backup = MemoryHelper.ReadRaw((nint)((GameObject*)DalamudApi.ObjectTable[0].Address)->Name, 64);
                if (DalamudApi.Configuration.FakeName[0] == 0)
                {
                    CopyBytes( DalamudApi.Configuration.FakeName,backup, 64);
                }
                CopyBytes(inputs, DalamudApi.Configuration.FakeName, 64);
                //CopyBytes(inputs, backup, 64);
                CopyBytes(target, inputs, 64);
                Refresh();
            }
        }
        public bool isUIShow = false;
        public void OpenUI()
        {
            isUIShow = true;
        }

        public void DrawUI()
        {
            if (!isUIShow)
            {
                return;
            }
            if (ImGui.Begin("Chameleon", ref this.isUIShow, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGui.Text($"OrignalName: {System.Text.Encoding.UTF8.GetString(backup)}");
                ImGui.SameLine();
                ImGui.Text($"-> FakeName: {System.Text.Encoding.UTF8.GetString(target)}");
                ImGui.Text("PlayerName: "); ImGui.SameLine();
                if (ImGui.InputText("##PlayerName", inputs, 64)) { }

                if (ImGui.Button("Apply"))
                {
                    CopyBytes(DalamudApi.Configuration.FakeName, inputs, 64);
                    DalamudApi.Configuration.Save();
                    CopyBytes(target, inputs, 64);
                    Refresh();
                }
                ImGui.SameLine();
                if (ImGui.Button("SetAsOri"))
                {
                    CopyBytes(backup, inputs, 64);
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset"))
                {
                    CopyBytes(target, backup, 64);
                    Refresh();
                }

            }
        }

        private unsafe void Refresh()
        {
            if (!DalamudApi.ClientState.LocalPlayer || !DalamudApi.ClientState.IsLoggedIn) return;
            if (SpecialName != IntPtr.Zero) MemoryHelper.WriteRaw(SpecialName, target);
            var ps = PlayerState.Instance();
            var pc = DalamudApi.ClientState.LocalPlayer?.Address;
            var ptr = stackalloc byte[64];
            for (int i = 0; i < 64; i++) ptr[i] = target[i];
            if ((nint)ps != IntPtr.Zero && UpdatePlayerStatusName != IntPtr.Zero)
                UpdatePlayerStatusNameDetour((nint)ps, (nint)ptr);
            if ((nint)pc != IntPtr.Zero && UpdatePlayerCharaName != IntPtr.Zero)
                UpdatePlayerCharaNameDetour((nint)pc, (nint)ptr);
        }
        public void CopyBytes(byte[] dst, byte[] src, int len)
        {
            for (int i = 0; i < len; i++) dst[i] = src[i];
        }
        public void Dispose()
        {
            UpdatePlayerCharaNameHook.Disable();
            UpdatePlayerStatusNameHook.Disable();
            for (int i = 0; i < 64; i++) target[i] = backup[i];
            Refresh();
            DalamudApi.Dispose();
        }

    }


    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public byte[] FakeName { get; set; } = new byte[64];

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private DalamudPluginInterface? PluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}
