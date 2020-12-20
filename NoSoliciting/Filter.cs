﻿using Dalamud.Game.Chat;
using Dalamud.Game.Chat.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Plugin;
using System;
using System.Runtime.InteropServices;

namespace NoSoliciting {
    public partial class Filter : IDisposable {
        private readonly Plugin plugin;
        private bool clearOnNext = false;

        private delegate void HandlePFPacketDelegate(IntPtr param_1, IntPtr param_2);
        private readonly Hook<HandlePFPacketDelegate> handlePacketHook;

        private delegate long HandlePFSummary2Delegate(long param_1, long param_2, byte param_3);
        private readonly Hook<HandlePFSummary2Delegate> handleSummaryHook;

        private bool disposedValue;

        public Filter(Plugin plugin) {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin), "Plugin cannot be null");

            var listingPtr = this.plugin.Interface.TargetModuleScanner.ScanText("40 53 41 57 48 83 EC 28 48 8B D9");
            var summaryPtr = this.plugin.Interface.TargetModuleScanner.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B FA 48 8B F1 45 84 C0 74 ?? 0F B7 0A");
            if (listingPtr == IntPtr.Zero || summaryPtr == IntPtr.Zero) {
                PluginLog.Log("Party Finder filtering disabled because hook could not be created.");
                return;
            }

            this.handlePacketHook = new Hook<HandlePFPacketDelegate>(listingPtr, new HandlePFPacketDelegate(this.HandlePFPacket));
            this.handlePacketHook.Enable();

            this.handleSummaryHook = new Hook<HandlePFSummary2Delegate>(summaryPtr, new HandlePFSummary2Delegate(this.HandleSummary));
            this.handleSummaryHook.Enable();
        }

        private void HandlePFPacket(IntPtr param_1, IntPtr param_2) {
            if (this.plugin.Definitions == null) {
                this.handlePacketHook.Original(param_1, param_2);
                return;
            }

            if (this.clearOnNext) {
                this.plugin.ClearPartyFinderHistory();
                this.clearOnNext = false;
            }

            var dataPtr = param_2 + 0x10;

            // parse the packet into a struct
            var packet = Marshal.PtrToStructure<PfPacket>(dataPtr);

            for (var i = 0; i < packet.listings.Length; i++) {
                var listing = packet.listings[i];

                // only look at listings that aren't null
                if (listing.IsNull()) {
                    continue;
                }

                var desc = listing.Description();

                string reason = null;
                var filter = false;

                filter = filter || (this.plugin.Config.FilterHugeItemLevelPFs
                    && listing.minimumItemLevel > FilterUtil.MaxItemLevelAttainable(this.plugin.Interface.Data)
                    && SetReason(ref reason, "ilvl"));

                foreach (var def in this.plugin.Definitions.PartyFinder.Values) {
                    filter = filter || (this.plugin.Config.FilterStatus.TryGetValue(def.Id, out var enabled)
                        && enabled
                        && def.Matches(XivChatType.None, desc)
                        && SetReason(ref reason, def.Id));
                }

                // check for custom filters if enabled
                filter = filter || (this.plugin.Config.CustomPFFilter
                    && PartyFinder.MatchesCustomFilters(desc, this.plugin.Config)
                    && SetReason(ref reason, "custom"));

                this.plugin.AddPartyFinderHistory(new Message(
                    defs: this.plugin.Definitions,
                    type: ChatType.None,
                    sender: listing.Name(),
                    content: listing.Description(),
                    reason: reason
                ));

                if (!filter) {
                    continue;
                }

                // replace the listing with an empty one
                packet.listings[i] = new PfListing();

                PluginLog.Log($"Filtered PF listing from {listing.Name()} ({reason}): {listing.Description()}");
            }

            // get some memory for writing to
            var newPacket = new byte[PacketInfo.PacketSize];
            var pinnedArray = GCHandle.Alloc(newPacket, GCHandleType.Pinned);
            var pointer = pinnedArray.AddrOfPinnedObject();

            // write our struct into the memory (doing this directly crashes the game)
            Marshal.StructureToPtr(packet, pointer, false);

            // copy our new memory over the game's
            Marshal.Copy(newPacket, 0, dataPtr, PacketInfo.PacketSize);

            // free memory
            pinnedArray.Free();

            // call original function
            this.handlePacketHook.Original(param_1, param_2);
        }

        private long HandleSummary(long param_1, long param_2, byte param_3) {
            this.clearOnNext = true;

            return this.handleSummaryHook.Original(param_1, param_2, param_3);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "fulfilling a delegate")]
        public void OnChat(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message), "SeString cannot be null");
            }

            if (this.plugin.Definitions == null || ChatTypeExt.FromDalamud(type).IsBattle()) {
                return;
            }

            var text = message.TextValue;

            string reason = null;
            var filter = false;

            foreach (var def in this.plugin.Definitions.Chat.Values) {
                filter = filter || (this.plugin.Config.FilterStatus.TryGetValue(def.Id, out var enabled)
                    && enabled
                    && def.Matches(type, text)
                    && SetReason(ref reason, def.Id));
            }

            // check for custom filters if enabled
            filter = filter || (this.plugin.Config.CustomChatFilter
                && Chat.MatchesCustomFilters(text, this.plugin.Config)
                && SetReason(ref reason, "custom"));

            this.plugin.AddMessageHistory(new Message(
                defs: this.plugin.Definitions,
                type: ChatTypeExt.FromDalamud(type),
                sender: sender,
                content: message,
                reason: reason
            ));

            if (!filter) {
                return;
            }

            PluginLog.Log($"Filtered chat message ({reason}): {text}");
            isHandled = true;
        }

        private static bool SetReason(ref string reason, string value) {
            reason = value;
            return true;
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    this.handlePacketHook?.Dispose();
                    this.handleSummaryHook?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
