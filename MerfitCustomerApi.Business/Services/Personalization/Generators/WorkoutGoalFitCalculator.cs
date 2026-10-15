using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Personalization.Generators;

/// <summary>
/// Bir antrenmanin ozellik puanlarindan (WorkoutAttributeScore), kullanicinin hedef agirlik profiline
/// gore 0-100 arasi hedef uyum puanini (FitScore) hesaplar:
///
///     FitScore = (Hipertrofi * W_h + Kuvvet * W_s + Kardiyo * W_c) / 100
///
/// Yalnizca uc ana ozellik kullanilir (bkz. <see cref="GoalAttributeWeights"/>); Endurance, Mobility ve
/// CoreStability puanlari yok sayilir.
///
/// Eksik puan stratejisi:
/// - Ana ozelliklerden biri puanlanmamissa 0 kabul edilir; agirliklar yeniden normalize EDILMEZ. Onceki
///   surumdeki normalizasyon, yalnizca tek bir ozelligi (orn. sadece Hipertrofi = 95) puanlanmis bir
///   antrenmanin tam puanli antrenmanlarin onune gecmesine yol aciyordu. Eksik puan artik antrenmani
///   yukseltmez, yalnizca kismi katki verir; admin ekrani uc ana puanin birlikte girilmesini ister.
/// - Uc ana ozelligin hicbiri puanlanmamissa FitScore hesaplanamaz ve null doner; cagiran taraf bu
///   antrenmana hedef uyum katkisi vermez (eski kriterlerle siralanir).
/// </summary>
public static class WorkoutGoalFitCalculator
{
    public static decimal? Calculate(
        IReadOnlyDictionary<WorkoutAttributeType, int> attributeScores,
        GoalAttributeWeights weights)
    {
        var weightedSum = 0m;
        var anyPrimaryScored = false;

        foreach (var attributeType in GoalAttributeWeights.PrimaryAttributes)
        {
            if (!attributeScores.TryGetValue(attributeType, out var score))
            {
                continue;
            }

            anyPrimaryScored = true;
            weightedSum += score * weights.GetWeight(attributeType);
        }

        return anyPrimaryScored ? weightedSum / GoalAttributeWeights.RequiredTotal : null;
    }

    /// <summary>Uc ana ozelligin tamami puanlanmis mi.</summary>
    public static bool HasAllPrimaryScores(IReadOnlyDictionary<WorkoutAttributeType, int> attributeScores) =>
        GoalAttributeWeights.PrimaryAttributes.All(attributeScores.ContainsKey);
}
