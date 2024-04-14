using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ImGuiNET;
using System;
using System.Reflection;

namespace Chameleon
{
    public class Chameleon : IDalamudPlugin
    {
        public string Name => "Chameleon";
        private DalamudPluginInterface PluginInterface;
        public Configuration Configuration;
        private Assembly DalamudAssembly = null;

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
            for (int i = 0; i < 64; i++)
                ptr[i] = target[i];
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
            if (Svc.ClientState.LocalPlayer is not null && address == Svc.ClientState.LocalPlayer.Address && target[0] != 0)
            {
                Svc.Log.Info($"Faking: {System.Text.Encoding.UTF8.GetString(target)}");
                var ptr = stackalloc byte[64];
                for (int i = 0; i < 64; i++)
                    ptr[i] = target[i];
                return UpdatePlayerCharaNameHook.Original(address, (nint)ptr);
            }
            else
                return UpdatePlayerCharaNameHook.Original(address, name);
        }
        public IntPtr SpecialName { get; private set; }
        #endregion


        public Chameleon(DalamudPluginInterface pluginInterface)
        {
            PluginInterface = pluginInterface;
            ECommonsMain.Init(pluginInterface, this);
            Configuration = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(Svc.PluginInterface);
            Svc.PluginInterface.UiBuilder.OpenConfigUi += OpenUI;
            Svc.PluginInterface.UiBuilder.OpenMainUi += OpenUI;
            Svc.PluginInterface.UiBuilder.Draw += DrawUI;
            DalamudAssembly = Svc.PluginInterface.GetType().Assembly;

            SpecialName = Svc.SigScanner.GetStaticAddressFromSig("0f ?? ?? ?? ?? ?? ?? 89 4c 24 ?? 40");
            UpdatePlayerStatusName = Svc.SigScanner.ScanText("48 89 5c 24 ?? 48 89 74 24 ?? 57 48 ?? ?? ?? ?? ?? ?? 48 ?? ?? ?? ?? ?? ?? 48 ?? ?? 48 89 84 24 ?? ?? ?? ?? 48 ?? ?? 48 ?? ?? ?? 0f 1f 44 00");
            Svc.Log.Info($"UpdatePlayerStatusName: {UpdatePlayerStatusName:X}");
            UpdatePlayerStatusNameHook = Svc.Hook.HookFromAddress<UpdatePlayerStatusNameDelegate>(UpdatePlayerStatusName, UpdatePlayerStatusNameDetour);
            UpdatePlayerStatusNameHook?.Enable();

            UpdatePlayerCharaName = Svc.SigScanner.ScanText("40 ?? 48 ?? ?? ?? 48 ?? ?? 48 ?? ?? 0f 84 ?? ?? ?? ?? 32");
            Svc.Log.Info($"UpdatePlayerCharaName: {UpdatePlayerCharaName:X}");
            UpdatePlayerCharaNameHook = Svc.Hook.HookFromAddress<UpdatePlayerCharaNameDelegate>(UpdatePlayerCharaName, UpdatePlayerCharaNameDetour);
            UpdatePlayerCharaNameHook?.Enable();

            Init();
            Svc.ClientState.Login += LoginInit;
        }

        public void LoginInit() => Init();
        public unsafe void Init()
        {
            if (Svc.ClientState.IsLoggedIn)
            {
                backup = MemoryHelper.ReadRaw((nint)Player.GameObject->Name, 64);
                if (Configuration.FakeName[0] == 0)
                {
                    CopyBytes(Configuration.FakeName, backup, 64);
                }
                CopyBytes(inputs, Configuration.FakeName, 64);
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
            if (ImGui.Begin("Chameleon", ref isUIShow, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                ImGui.Text($"OrignalName: {System.Text.Encoding.UTF8.GetString(backup)}");
                ImGui.SameLine(0, 0);
                ImGui.Text($" -> FakeName: {System.Text.Encoding.UTF8.GetString(target)}");
                ImGui.InputText("PlayerName##PlayerName", inputs, 64);

                if (ImGui.Button("Apply"))
                {
                    CopyBytes(Configuration.FakeName, inputs, 64);
                    Configuration.Save();
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
            if (Svc.ClientState.LocalPlayer is null)
                return;
            if (SpecialName != IntPtr.Zero)
                MemoryHelper.WriteRaw(SpecialName, target);
            var ps = PlayerState.Instance();
            var pc = Svc.ClientState.LocalPlayer.Address;
            var ptr = stackalloc byte[64];
            for (int i = 0; i < 64; i++)
                ptr[i] = target[i];
            if ((nint)ps != IntPtr.Zero && UpdatePlayerStatusName != IntPtr.Zero)
                UpdatePlayerStatusNameDetour((nint)ps, (nint)ptr);
            if (pc != IntPtr.Zero && UpdatePlayerCharaName != IntPtr.Zero)
                UpdatePlayerCharaNameDetour(pc, (nint)ptr);
        }

        public static void CopyBytes(byte[] dst, byte[] src, int len)
        {
            for (int i = 0; i < len; i++)
                dst[i] = src[i];
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            for (int i = 0; i < 64; i++)
                target[i] = backup[i];
            Refresh();
            UpdatePlayerCharaNameHook?.Dispose();
            UpdatePlayerStatusNameHook?.Dispose();
            ECommonsMain.Dispose();
        }
    }
}
