namespace Lib9c.Tests.Action.Guild
{
    using System.Collections.Generic;
    using Lib9c.Tests.Util;
    using Libplanet.Action.State;
    using Libplanet.Crypto;
    using Nekoyume.Action.Guild;
    using Nekoyume.Model.Guild;
    using Nekoyume.Module.Guild;
    using Xunit;

    public class MakeGuildTest : GuildTestBase
    {
        public static IEnumerable<object[]> TestCases =>
            new[]
            {
                new object[]
                {
                    AddressUtil.CreateAgentAddress(),
                    // TODO: Update to false when Guild features are enabled.
                    true,
                },
                new object[]
                {
                    GuildConfig.PlanetariumGuildOwner,
                    false,
                },
            };

        [Fact]
        public void Serialization()
        {
            var action = new MakeGuild();
            var plainValue = action.PlainValue;

            var deserialized = new MakeGuild();
            deserialized.LoadPlainValue(plainValue);
        }

        [Fact]
        public void Execute()
        {
            IWorld world = World;
            var validatorPrivateKey = new PrivateKey();
            var guildMasterAddress = AddressUtil.CreateAgentAddress();
            world = EnsureToMintAsset(world, validatorPrivateKey.Address, GG * 100);
            world = EnsureToCreateValidator(world, validatorPrivateKey.PublicKey);
            var action = new MakeGuild(validatorPrivateKey.Address);

            world = action.ExecutePublic(new ActionContext
            {
                PreviousState = world,
                Signer = guildMasterAddress,
            });

            var repository = new GuildRepository(world, new ActionContext());
            var guildAddress = repository.GetJoinedGuild(guildMasterAddress);
            Assert.NotNull(guildAddress);
            var guild = repository.GetGuild(guildAddress.Value);
            Assert.Equal(guildMasterAddress, guild.GuildMasterAddress);
        }
    }
}
