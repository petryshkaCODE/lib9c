using System.Linq;
using Bencodex.Types;
using Libplanet.Action.State;
using Libplanet.Action;
using Libplanet.Types.Consensus;
using Nekoyume.ValidatorDelegation;
using Nekoyume.Model.Guild;
using Nekoyume.Action.Guild.Migration.LegacyModels;

namespace Nekoyume.Action.ValidatorDelegation
{
    public sealed class UpdateValidators : ActionBase
    {
        public UpdateValidators() { }

        public override IValue PlainValue => Null.Value;

        public override void LoadPlainValue(IValue plainValue)
        {
        }

        public override IWorld Execute(IActionContext context)
        {
            var world = context.PreviousState;

            if (world.GetDelegationMigrationHeight() is null)
            {
                return world;
            }

            var prevValidators = world.GetValidatorSet().Validators;
            var repository = new ValidatorRepository(world, context);
            var validators = repository.GetValidatorList().ActiveSet();

            foreach (var deactivated in prevValidators.Select(v => v.OperatorAddress)
                .Except(validators.Select(v => v.OperatorAddress)))
            {
                var validatorDelegatee = repository.GetValidatorDelegatee(deactivated);
                validatorDelegatee.Deactivate();
                repository.SetValidatorDelegatee(validatorDelegatee);
                var guildRepository = new GuildRepository(repository.World, repository.ActionContext);
                var guildDelegatee = guildRepository.GetGuildDelegatee(deactivated);
                guildDelegatee.Deactivate();
                guildRepository.SetGuildDelgatee(guildDelegatee);
                repository.UpdateWorld(guildRepository.World);
            }

            foreach (var activated in validators.Select(v => v.OperatorAddress)
                .Except(prevValidators.Select(v => v.OperatorAddress)))
            {
                var validatorDelegatee = repository.GetValidatorDelegatee(activated);
                validatorDelegatee.Activate();
                repository.SetValidatorDelegatee(validatorDelegatee);
                var guildRepository = new GuildRepository(repository.World, repository.ActionContext);
                var guildDelegatee = guildRepository.GetGuildDelegatee(activated);
                guildDelegatee.Activate();
                guildRepository.SetGuildDelgatee(guildDelegatee);
                repository.UpdateWorld(guildRepository.World);
            }

            return repository.World.SetValidatorSet(new ValidatorSet(validators));
        }
    }
}