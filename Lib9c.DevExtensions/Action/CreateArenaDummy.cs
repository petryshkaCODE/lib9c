using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using Nekoyume.Action;
using Nekoyume.Extensions;
using Nekoyume.Model.Arena;
using Nekoyume.Model.State;
using Nekoyume.Module;
using Nekoyume.TableData;

namespace Lib9c.DevExtensions.Action
{
    [Serializable]
    [ActionType("create_arena_dummy")]
    public class CreateArenaDummy : GameAction
    {
        public Address myAvatarAddress;
        public int accountCount;
        public int championshipId;
        public int round;
        public List<Guid> costumes;
        public List<Guid> equipments;

        protected override IImmutableDictionary<string, IValue> PlainValueInternal =>
            new Dictionary<string, IValue>()
            {
                ["myAvatarAddress"] = myAvatarAddress.Serialize(),
                ["accountCount"] = championshipId.Serialize(),
                ["championshipId"] = championshipId.Serialize(),
                ["round"] = round.Serialize(),
                ["costumes"] = new List(costumes
                    .OrderBy(element => element).Select(e => e.Serialize())),
                ["equipments"] = new List(equipments
                    .OrderBy(element => element).Select(e => e.Serialize())),
            }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(
            IImmutableDictionary<string, IValue> plainValue)
        {
            myAvatarAddress = plainValue["myAvatarAddress"].ToAddress();
            accountCount = plainValue["accountCount"].ToInteger();
            championshipId = plainValue["championshipId"].ToInteger();
            round = plainValue["round"].ToInteger();
            costumes = ((List)plainValue["costumes"]).Select(e => e.ToGuid()).ToList();
            equipments = ((List)plainValue["equipments"]).Select(e => e.ToGuid()).ToList();
        }

        public override IWorld Execute(IActionContext context)
        {
            GasTracer.UseGas(1);
            var states = context.PreviousState;

            for (var i = 0; i < accountCount; i++)
            {
                var privateKey = new PrivateKey();
                var agentAddress = privateKey.PublicKey.Address;
                var avatarAddress = agentAddress.Derive(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        CreateAvatar.DeriveFormat,
                        0
                    )
                );

                var existingAgentState = states.GetAgentState(agentAddress);
                var agentState = existingAgentState ?? new AgentState(agentAddress);
                var avatarState = states.GetAvatarState(avatarAddress);
                if (!(avatarState is null))
                {
                    throw new InvalidAddressException(
                        $"Aborted as there is already an avatar at {avatarAddress}.");
                }

                if (agentState.avatarAddresses.ContainsKey(0))
                {
                    throw new AvatarIndexAlreadyUsedException(
                        $"Aborted as the signer already has an avatar at index #{0}.");
                }

                agentState.avatarAddresses.Add(0, avatarAddress);

                var rankingState = context.PreviousState.GetRankingState();
                var rankingMapAddress = rankingState.UpdateRankingMap(avatarAddress);

                // create ArenaScore
                var sheets = states.GetSheets(
                    new[]
                    {
                        typeof(ItemRequirementSheet),
                        typeof(EquipmentItemRecipeSheet),
                        typeof(EquipmentItemSubRecipeSheetV2),
                        typeof(EquipmentItemOptionSheet),
                        typeof(CostumeItemSheet),
                        typeof(EquipmentItemSheet),
                        typeof(MaterialItemSheet),
                        typeof(ArenaSheet),
                        typeof(SkillSheet),
                    });

                avatarState = TestbedHelper.CreateAvatarState(avatarAddress.ToHex().Substring(0, 4),
                    agentAddress,
                    avatarAddress,
                    context.BlockIndex,
                    context.PreviousState.GetAvatarSheets(),
                    context.PreviousState.GetSheet<WorldSheet>(),
                    context.PreviousState.GetGameConfigState(),
                    rankingMapAddress);

                if (!states.TryGetAvatarState(context.Signer, myAvatarAddress, out var myAvatarState))
                {
                    throw new FailedLoadStateException($"error");
                }

                // copy item
                foreach (var item in myAvatarState.inventory.Items)
                {
                    avatarState.inventory.AddItem(item.item);
                }

                // join arena
                states = states
                    .SetAgentState(agentAddress, agentState)
                    .SetAvatarState(avatarAddress, avatarState);


                var sheet = sheets.GetSheet<ArenaSheet>();
                if (!sheet.TryGetValue(championshipId, out var row))
                {
                    throw new SheetRowNotFoundException(
                        nameof(ArenaSheet), $"championship Id : {championshipId}");
                }

                if (!row.TryGetRound(round, out var roundData))
                {
                    throw new RoundNotFoundException(
                        $"[{nameof(CreateArenaDummy)}] ChampionshipId({row.ChampionshipId}) - round({round})");
                }

                var arenaScoreAdr =
                    ArenaScore.DeriveAddress(avatarAddress, roundData.ChampionshipId,
                        roundData.Round);
                if (states.TryGetLegacyState(arenaScoreAdr, out List _))
                {
                    throw new ArenaScoreAlreadyContainsException(
                        $"[{nameof(CreateArenaDummy)}] id({roundData.ChampionshipId}) / round({roundData.Round})");
                }

                var arenaScore = new ArenaScore(avatarAddress, roundData.ChampionshipId,
                    roundData.Round);

                // create ArenaInformation
                var arenaInformationAdr =
                    ArenaInformation.DeriveAddress(avatarAddress, roundData.ChampionshipId,
                        roundData.Round);
                if (states.TryGetLegacyState(arenaInformationAdr, out List _))
                {
                    throw new ArenaInformationAlreadyContainsException(
                        $"[{nameof(CreateArenaDummy)}] id({roundData.ChampionshipId}) / round({roundData.Round})");
                }

                var arenaInformation =
                    new ArenaInformation(avatarAddress, roundData.ChampionshipId, roundData.Round);

                // update ArenaParticipants
                var arenaParticipantsAdr =
                    ArenaParticipants.DeriveAddress(roundData.ChampionshipId, roundData.Round);
                var arenaParticipants = states.GetArenaParticipants(arenaParticipantsAdr,
                    roundData.ChampionshipId, roundData.Round);
                arenaParticipants.Add(avatarAddress);

                // update ArenaAvatarState
                var arenaAvatarStateAdr = ArenaAvatarState.DeriveAddress(avatarAddress);
                var arenaAvatarState = states.GetArenaAvatarState(arenaAvatarStateAdr, avatarState);
                arenaAvatarState.UpdateCostumes(costumes);
                arenaAvatarState.UpdateEquipment(equipments);

                states = states
                    .SetLegacyState(arenaScoreAdr, arenaScore.Serialize())
                    .SetLegacyState(arenaInformationAdr, arenaInformation.Serialize())
                    .SetLegacyState(arenaParticipantsAdr, arenaParticipants.Serialize())
                    .SetLegacyState(arenaAvatarStateAdr, arenaAvatarState.Serialize())
                    .SetAgentState(agentAddress, agentState);
            }

            return states;
        }
    }
}
