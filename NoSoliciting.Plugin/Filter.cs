using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
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

        private bool _suppressNextBubble = false;
        
        private readonly Hook<RaptureLogModule.Delegates.ShowMiniTalkPlayer>? _showMiniTalkPlayerHook;

        public Filter(Plugin plugin) {
            this.Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin), "Plugin cannot be null");
            unsafe
            {
                _showMiniTalkPlayerHook = this.Plugin.GameInteropProvider.HookFromAddress<RaptureLogModule.Delegates.ShowMiniTalkPlayer>(
                    RaptureLogModule.MemberFunctionPointers.ShowMiniTalkPlayer, OnBubble);
                _showMiniTalkPlayerHook!.Enable();
                
            }

            this.Plugin.ChatGui.CheckMessageHandled += this.OnChat;
            this.Plugin.PartyFinderGui.ReceiveListing += this.OnListing;
        }

        private void Dispose(bool disposing) {
            if (this._disposedValue) {
                return;
            }

            if (disposing) {
                this.Plugin.ChatGui.CheckMessageHandled -= this.OnChat;
                this.Plugin.PartyFinderGui.ReceiveListing -= this.OnListing;
                this._showMiniTalkPlayerHook?.Dispose();
            }

            this._disposedValue = true;
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void OnChat(IHandleableChatMessage message) {
            if (message.IsHandled)
                return;
            else if (this.FilterMessage(message))
            {
                message.PreventOriginal();
            }
            
            //Chat bubbles are fired even if we set isHandled true,which causes the last chat message
            //to be the bubble,so we need to suppress the next bubble call
            //unless your chat is under super heavy load,there should not be a problem with this
            if (message.IsHandled)
            {
                _suppressNextBubble = true;
            }
        }

        private unsafe void OnBubble(RaptureLogModule* rapture, ushort logKindId, Utf8String* sender, Utf8String*
            message, ushort worldId, bool isLocalPlayer)
        {
            if (_suppressNextBubble)
            {
                _suppressNextBubble = false;
            }
            else
            {
                //don't need to check anything else, if user has bubbles off this hook is never called anyway
                _showMiniTalkPlayerHook!.Original(rapture, logKindId, sender, message, worldId, isLocalPlayer);
            }
        }

        private void OnListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args) {
            try {
                if (this.LastBatch != args.BatchNumber) {
                    this.Plugin.ClearPartyFinderHistory();
                }

                this.LastBatch = args.BatchNumber;

                var version = this.Plugin.MlFilter?.Version;
                var (category, reason) = this.MlListingFilterReason(listing);

                this.Plugin.AddPartyFinderHistory(new Message(
                    version,
                    ChatType.None,
                    listing.Name,
                    listing.Description,
                    category,
                    reason == "custom",
                    reason == "ilvl",
                    this.Plugin.Config.CreateFiltersClone()
                ));

                if (category == null && reason == null) {
                    return;
                }

                args.Visible = false;

                if (this.Plugin.Config.LogFilteredPfs) {
                    Plugin.Log.Info($"Filtered PF listing from {listing.Name.TextValue} ({reason}): {listing.Description.TextValue}");
                }
            } catch (Exception ex) {
                Plugin.Log.Error($"Error in PF listing event: {ex}");
            }
        }

        private bool FilterMessage(IHandleableChatMessage message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message), "SeString cannot be null");
            }

            return this.MlFilterMessage(message);
        }

        private bool MlFilterMessage(IHandleableChatMessage message) {
            var chatType = ChatTypeExt.FromDalamud(message.LogKind);

            // NOTE: don't filter on user-controlled chat types here because custom filters are supposed to check all
            //       messages except battle messages
            if (chatType.IsBattle()) {
                return false;
            }
            
            // don't filter own chat messages
            var playerName = Plugin.PlayerState.CharacterName;
            if (message.Sender != null && 
                !string.IsNullOrEmpty(message.Sender.TextValue) && 
                !string.IsNullOrEmpty(playerName) &&
                message.Sender.TextValue.Equals(playerName)) {
                Plugin.Log.Verbose("Skip filtering own message for character: " + playerName);
                return false;
            }

            var text = message.Message.TextValue;

            var custom = false;
            MessageCategory? classification = null;

            // step 1. check for custom filters if enabled
            var filter = false;
            if (this.Plugin.Config.CustomChatFilter && Chat.MatchesCustomFilters(text, this.Plugin.Config)) {
                filter = true;
                custom = true;
            }

            // only look at ml if message >= min words
            if (!filter && this.Plugin.MlFilter != null && CountWords(text) >= MinWords) {
                // step 2. classify the message using the model
                var category = this.Plugin.MlFilter.ClassifyMessage((ushort) chatType, text);

                // step 2a. only filter if configured to act on this channel
                if (category != MessageCategory.Normal && this.Plugin.Config.MlEnabledOn(category, chatType)) {
                    filter = true;
                    classification = category;
                }
            }

            var history = new Message(
                this.Plugin.MlFilter?.Version,
                ChatTypeExt.FromDalamud(message.LogKind),
                // (uint)senderId, //TODO: messages don't provide that anymore, figure something out
                message.Sender ?? SeString.Empty,
                message.Message,
                classification,
                custom,
                false,
                this.Plugin.Config.CreateFiltersClone()
            );
            this.Plugin.AddMessageHistory(history);

            if (filter && this.Plugin.Config.LogFilteredChat) {
                Plugin.Log.Info($"Filtered chat message ({history.FilterReason ?? "unknown"}): {text}");
            }

            return filter;
        }

        private (MessageCategory?, string?) MlListingFilterReason(IPartyFinderListing listing) {
            if (this.Plugin.MlFilter == null) {
                return (null, null);
            }

            // ignore private listings if configured
            if (!this.Plugin.Config.ConsiderPrivatePfs && listing[SearchAreaFlags.Private]) {
                return (null, null);
            }

            // step 1. check if pf has an item level that's too high
            if (this.Plugin.Config.FilterHugeItemLevelPFs && listing.MinimumItemLevel > FilterUtil.MaxItemLevelAttainable(this.Plugin.DataManager)) {
                return (null, "ilvl");
            }

            var desc = listing.Description.TextValue;

            // step 2. check custom filters
            if (this.Plugin.Config.CustomPFFilter && PartyFinder.MatchesCustomFilters(desc, this.Plugin.Config)) {
                return (null, "custom");
            }

            // only look at ml for pfs >= min words
            if (CountWords(desc) < MinWords) {
                return (null, null);
            }

            var category = this.Plugin.MlFilter.ClassifyMessage((ushort) ChatType.None, desc);

            if (category != MessageCategory.Normal && this.Plugin.Config.MlEnabledOn(category, ChatType.None)) {
                return (category, Enum.GetName(category));
            }

            return (null, null);
        }

        private static int CountWords(string text) {
            return text.Spacify().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
