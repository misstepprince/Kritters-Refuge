// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 gluesniffler <linebarrelerenthusiast@gmail.com>
// SPDX-FileCopyrightText: 2025 vanx <61917534+Vaaankas@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Managers;
using Content.Server.IdentityManagement;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using System.Globalization;
using Content.Shared.CCVar;

namespace Content.Server._White.Examine
{
    public sealed partial class ExaminableCharacterSystem : EntitySystem
    {
        [Dependency] private IChatManager _chatManager = default!;
        [Dependency] private INetConfigurationManager _netConfigManager = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<MetaDataComponent, ExamineCompletedEvent>(HandleExamine);
        }

        private void HandleExamine(EntityUid uid, MetaDataComponent metaData, ExamineCompletedEvent args)
        {
            if (TryComp<ActorComponent>(args.Examiner, out var actorComponent)
                && _netConfigManager.GetClientCVar(actorComponent.PlayerSession.Channel, CCVars.LogInChat))
            {
                var logLines = new List<string>();

                if (!args.IsSecondaryInfo)
                {
                    TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
                    var entTextName = textInfo.ToTitleCase(metaData.EntityName);
                    if (!string.IsNullOrEmpty(args.HeaderModifier))
                    {
                        entTextName = $"{entTextName} {args.HeaderModifier}";
                    }
                    logLines.Add(
                        $"[color=DarkGray][font size=11]{entTextName}[/font][/color]");
                }

                logLines.Add($"[color=DarkGray][font size=10]{args.Message}[/font][/color]");
                var combinedLog = string.Join("\n", logLines);
                _chatManager.ChatMessageToOne(
                    ChatChannel.Emotes,
                    combinedLog,
                    combinedLog,
                    EntityUid.Invalid,
                    false,
                    actorComponent.PlayerSession.Channel,
                    recordReplay: false);
            }
        }
    }
}
