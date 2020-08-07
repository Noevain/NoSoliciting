﻿using Dalamud.Game.Chat;
using Dalamud.Game.Chat.SeStringHandling;
using Dalamud.Game.Internal.Network;
using Dalamud.Plugin;
using System;
using System.Runtime.InteropServices;

namespace NoSoliciting {
    public partial class RMTDetection {
        private const ushort PF_LISTING = 0x122;
        //private static ushort PF_SUMMARY = 0x127;

        private readonly Plugin plugin;

        public RMTDetection(Plugin plugin) {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin), "Plugin cannot be null");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "fulfilling a delegate")]
        public void OnNetwork(IntPtr dataPtr, ushort opCode, uint sourceActorId, uint targetActorId, NetworkMessageDirection direction) {
            // only filter when enabled
            if (!this.plugin.Config.FilterPartyFinder) {
                return;
            }

            // only look at packets coming in
            if (direction != NetworkMessageDirection.ZoneDown) {
                return;
            }

            // PF_LISTING is sent repeatedly until PF_SUMMARY, which is a summary (and also the packet sent for the chat notifs)
            if (opCode != PF_LISTING) {
                return;
            }

            // parse the packet into a struct
            PFPacket packet = Marshal.PtrToStructure<PFPacket>(dataPtr);

            for (int i = 0; i < packet.listings.Length; i++) {
                PFListing listing = packet.listings[i];

                // only look at listings that aren't null
                if (listing.IsNull()) {
                    continue;
                }

                string desc = listing.Description();

                // only look at listings that are RMT
                if (!PartyFinder.IsRMT(desc) && !PartyFinder.MatchesCustomFilters(desc, this.plugin.Config)) {
                    continue;
                }

                // replace the listing with an empty one
                packet.listings[i] = new PFListing();

                PluginLog.Log($"Filtered out PF listing from {listing.Name()}: {listing.Description()}");
            }

            // get some memory for writing to
            byte[] newPacket = new byte[PacketInfo.packetSize];
            GCHandle pinnedArray = GCHandle.Alloc(newPacket, GCHandleType.Pinned);
            IntPtr pointer = pinnedArray.AddrOfPinnedObject();

            // write our struct into the memory (doing this directly crashes the game)
            Marshal.StructureToPtr(packet, pointer, false);

            // copy our new memory over the game's
            Marshal.Copy(newPacket, 0, dataPtr, PacketInfo.packetSize);

            // free memory
            pinnedArray.Free();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "fulfilling a delegate")]
        public void OnChat(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message), "SeString cannot be null");
            }

            // only filter when enabled
            if (!this.plugin.Config.FilterChat) {
                return;
            }

            string text = message.TextValue;

            if (!Chat.IsRMT(text) && !Chat.MatchesCustomFilters(text, this.plugin.Config)) {
                return;
            }

            PluginLog.Log($"Handled RMT message: {text}");
            isHandled = true;
        }
    }
}
