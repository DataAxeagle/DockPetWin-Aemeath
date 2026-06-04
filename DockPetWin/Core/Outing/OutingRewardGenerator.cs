namespace DockPetWin.Core.Outing;

public sealed class OutingRewardGenerator
{
    private readonly OutingCatalog catalog;
    private readonly Random random = new();

    public OutingRewardGenerator(OutingCatalog catalog)
    {
        this.catalog = catalog;
    }

    public OutingReward? RewardForDuration(TimeSpan duration)
    {
        var rarity = DrawRarity(duration);
        if (rarity == 0)
        {
            return EventReward();
        }

        var matching = catalog.Collectables.Where(item => item.Rarity == rarity).ToList();
        var candidates = matching.Count > 0
            ? matching
            : catalog.Collectables.Where(item => item.Rarity == 1).ToList();
        return candidates.Count > 0
            ? new OutingReward.Collectable(candidates[random.Next(candidates.Count)])
            : EventReward();
    }

    public OutingReward? EventReward()
    {
        return catalog.Events.Count == 0
            ? null
            : new OutingReward.Event(catalog.Events[random.Next(catalog.Events.Count)]);
    }

    private int DrawRarity(TimeSpan duration)
    {
        var probabilities = RarityProbabilities(duration);
        var roll = Math.Clamp(random.NextDouble(), 0, 0.999999);
        var cumulative = 0.0;
        for (var rarity = 0; rarity <= 5; rarity++)
        {
            cumulative += probabilities.GetValueOrDefault(rarity);
            if (roll < cumulative)
            {
                return rarity;
            }
        }

        return 5;
    }

    private static Dictionary<int, double> RarityProbabilities(TimeSpan duration)
    {
        var minutes = Math.Max(0, duration.TotalMinutes);
        if (minutes < 25)
        {
            return new Dictionary<int, double> { [0] = 1, [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0 };
        }

        var eventT = Math.Clamp((minutes - 25) / (360 - 25), 0, 1);
        var eventProbability = 0.5 * (1 - eventT);
        var collectableProbability = 1 - eventProbability;
        var rarityT = Math.Clamp((minutes - 25) / (480 - 25), 0, 1);
        var startWeights = new Dictionary<int, double> { [1] = 60, [2] = 30, [3] = 7, [4] = 2, [5] = 1 };
        var targetShares = new Dictionary<int, double> { [1] = 0.15, [2] = 0.22, [3] = 0.30, [4] = 0.20, [5] = 0.13 };
        var startTotal = startWeights.Values.Sum();
        var weights = new Dictionary<int, double>();
        for (var rarity = 1; rarity <= 5; rarity++)
        {
            var startShare = startWeights[rarity] / startTotal;
            weights[rarity] = Math.Exp((1 - rarityT) * Math.Log(startShare) + rarityT * Math.Log(targetShares[rarity]));
        }

        var totalWeight = weights.Values.Sum();
        var result = new Dictionary<int, double> { [0] = eventProbability };
        for (var rarity = 1; rarity <= 5; rarity++)
        {
            result[rarity] = totalWeight > 0 ? collectableProbability * weights[rarity] / totalWeight : 0;
        }

        return result;
    }
}
