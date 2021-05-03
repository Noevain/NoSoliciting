﻿using Dalamud.Plugin;
using System;
using Dalamud.Game.Internal.Gui;
using Dalamud.Game.Internal.Gui.Structs;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using NoSoliciting.Interface;
using NoSoliciting.Ml;

namespace NoSoliciting {
    public partial class Filter : IDisposable {
        private const uint MinWords = 4;

        public static readonly ChatType[] FilteredChatTypes = {
            ChatType.Say,
            ChatType.Yell,
            ChatType.Shout,
            ChatType.TellIncoming,
            ChatType.Party,
            ChatType.CrossParty,
            ChatType.Alliance,
            ChatType.FreeCompany,
            ChatType.PvpTeam,
            ChatType.CrossLinkshell1,
            ChatType.CrossLinkshell2,
            ChatType.CrossLinkshell3,
            ChatType.CrossLinkshell4,
            ChatType.CrossLinkshell5,
            ChatType.CrossLinkshell6,
            ChatType.CrossLinkshell7,
            ChatType.CrossLinkshell8,
            ChatType.Linkshell1,
            ChatType.Linkshell2,
            ChatType.Linkshell3,
            ChatType.Linkshell4,
            ChatType.Linkshell5,
            ChatType.Linkshell6,
            ChatType.Linkshell7,
            ChatType.Linkshell8,
            ChatType.NoviceNetwork,
        };

        private Plugin Plugin { get; }
        private int LastBatch { get; set; } = -1;

        private bool _disposedValue;

        public Filter(Plugin plugin) {
            this.Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin), "Plugin cannot be null");

            this.Plugin.Interface.Framework.Gui.Chat.OnCheckMessageHandled += this.OnChat;
            this.Plugin.Interface.Framework.Gui.PartyFinder.ReceiveListing += this.OnListing;
        }

        private void Dispose(bool disposing) {
            if (this._disposedValue) {
                return;
            }

            if (disposing) {
                this.Plugin.Interface.Framework.Gui.Chat.OnCheckMessageHandled -= this.OnChat;
                this.Plugin.Interface.Framework.Gui.PartyFinder.ReceiveListing -= this.OnListing;
            }

            this._disposedValue = true;
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void OnChat(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) {
            isHandled = isHandled || this.FilterMessage(type, senderId, sender, message);
        }

        private void OnListing(PartyFinderListing listing, PartyFinderListingEventArgs args) {
            try {
                if (this.LastBatch != args.BatchNumber) {
                    this.Plugin.ClearPartyFinderHistory();
                }

                this.LastBatch = args.BatchNumber;

                var version = this.Plugin.MlFilter?.Version;
                var reason = this.MlListingFilterReason(listing);

                if (version == null) {
                    return;
                }

                this.Plugin.AddPartyFinderHistory(new Message(
                    version.Value,
                    ChatType.None,
                    listing.ContentIdLower,
                    listing.Name,
                    listing.Description,
                    true,
                    reason
                ));

                if (reason == null) {
                    return;
                }

                args.Visible = false;

                if (this.Plugin.Config.LogFilteredPfs) {
                    PluginLog.Log($"Filtered PF listing from {listing.Name.TextValue} ({reason}): {listing.Description.TextValue}");
                }
            } catch (Exception ex) {
                PluginLog.LogError($"Error in PF listing event: {ex}");
            }
        }

        private bool FilterMessage(XivChatType type, uint senderId, SeString sender, SeString message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message), "SeString cannot be null");
            }

            return this.MlFilterMessage(type, senderId, sender, message);
        }

        private bool MlFilterMessage(XivChatType type, uint senderId, SeString sender, SeString message) {
            if (this.Plugin.MlFilter == null) {
                return false;
            }

            var chatType = ChatTypeExt.FromDalamud(type);

            // NOTE: don't filter on user-controlled chat types here because custom filters are supposed to check all
            //       messages except battle messages
            if (chatType.IsBattle()) {
                return false;
            }

            var text = message.TextValue;

            string? reason = null;

            // step 1. check for custom filters if enabled
            var filter = this.Plugin.Config.CustomChatFilter
                         && Chat.MatchesCustomFilters(text, this.Plugin.Config)
                         && SetReason(out reason, "custom");

            // only look at ml if message >= min words
            if (!filter && text.Trim().Split(' ').Length >= MinWords) {
                // step 2. classify the message using the model
                var category = this.Plugin.MlFilter.ClassifyMessage((ushort) chatType, text);

                // step 2a. only filter if configured to act on this channel
                filter = category != MessageCategory.Normal
                         && this.Plugin.Config.MlEnabledOn(category, chatType)
                         && SetReason(out reason, category.Name());
            }

            this.Plugin.AddMessageHistory(new Message(
                this.Plugin.MlFilter.Version,
                ChatTypeExt.FromDalamud(type),
                senderId,
                sender,
                message,
                true,
                reason
            ));

            if (filter && this.Plugin.Config.LogFilteredChat) {
                PluginLog.Log($"Filtered chat message ({reason}): {text}");
            }

            return filter;
        }

        private string? MlListingFilterReason(PartyFinderListing listing) {
            if (this.Plugin.MlFilter == null) {
                return null;
            }

            // ignore private listings if configured
            if (!this.Plugin.Config.ConsiderPrivatePfs && listing[SearchAreaFlags.Private]) {
                return null;
            }

            var desc = listing.Description.TextValue;

            // step 1. check if pf has an item level that's too high
            if (this.Plugin.Config.FilterHugeItemLevelPFs && listing.MinimumItemLevel > FilterUtil.MaxItemLevelAttainable(this.Plugin.Interface.Data)) {
                return "ilvl";
            }

            // step 2. check custom filters
            if (this.Plugin.Config.CustomPFFilter && PartyFinder.MatchesCustomFilters(desc, this.Plugin.Config)) {
                return "custom";
            }

            // only look at ml for pfs >= min words
            if (desc.Trim().Spacify().Split(' ').Length < MinWords) {
                return null;
            }

            var category = this.Plugin.MlFilter.ClassifyMessage((ushort) ChatType.None, desc);

            if (category != MessageCategory.Normal && this.Plugin.Config.MlEnabledOn(category, ChatType.None)) {
                return category.Name();
            }

            return null;
        }

        private static bool SetReason(out string reason, string value) {
            reason = value;
            return true;
        }
    }
}
