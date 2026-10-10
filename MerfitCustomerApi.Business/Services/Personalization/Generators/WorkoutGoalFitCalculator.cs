using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Personalization.Generators;

/// <summary>
/// Bir antrenmanin ozellik puanlarindan (WorkoutAttributeScore), kullanicinin hedef agirlik profiline
/// gore 0-100 arasi hedef uyum puanini (FitScore) hesaplar:
///
///     FitScore = SUM(AttributeScore * GoalWeight) / SUM(GoalWeight)
///
/// Alti ozelligin tamami puanlanmissa payda 100 oldugundan bu, SUM(AttributeScore * GoalWeight / 100)
/// formuluyle birebir aynidir.
///
/// Eksik puan stratejisi:
/// - Bazi ozellikler puanlanmamissa eksik ozellikler varsayilan bir degerle DOLDURULMAZ; mevcut
///   ozelliklerin agirliklari yeniden normalize edilir (payda yalnizca puanlanmis ozelliklerin
///   agirlik toplamidir). Boylece sonuc yine 0-100 araliginda kalir.
/// - Hic puan kaydi yoksa (veya puanlanmis ozelliklerin hedefteki agirlik toplami 0 ise) FitScore
///   hesaplanamaz ve null doner; cagiran taraf bu antrenmana hedef uyum katkisi vermez.
/// </summary>
public static class WorkoutGoalFitCalculator
{
    public static decimal? Calculate(
        IReadOnlyDictionary<WorkoutAttributeType, int> attributeScores,
        GoalAttributeWeights weights)
    {
        var weightedSum = 0m;
        var coveredWeight = 0;

        foreach (var (attributeType, score) in attributeScores)
        {
            var weight = weights.GetWeight(attributeType);
            weightedSum += score * weight;
            coveredWeight += weight;
        }

        return coveredWeight == 0 ? null : weightedSum / coveredWeight;
    }
}
