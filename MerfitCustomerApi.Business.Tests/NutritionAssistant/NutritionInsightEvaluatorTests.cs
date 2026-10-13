using MerfitCustomerApi.Business.Services.NutritionAssistant;
using Xunit;
using static MerfitCustomerApi.Business.Tests.NutritionAssistant.NutritionAssistantTestData;
using Q = MerfitCustomerApi.Business.Services.NutritionAssistant.NutritionInsightEvaluator.Questions;
using S = MerfitCustomerApi.Business.Services.NutritionAssistant.NutritionInsightEvaluator.Statuses;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>Beslenme ekranindaki dinamik degerlendirmenin esikleri ve hizli soru secimi.</summary>
public class NutritionInsightEvaluatorTests
{
    [Fact]
    public void NoMeals_ReturnsNoDataMessage()
    {
        var insight = NutritionInsightEvaluator.Evaluate(Day(0, 0, 0, 0));

        Assert.Equal(S.NoData, insight.Status);
        Assert.Contains("henüz öğün kaydı yok", insight.Message);
    }

    [Fact]
    public void SpecExample_ProteinBehind_ReportsExactRemainingGrams()
    {
        var insight = NutritionInsightEvaluator.Evaluate(SpecExample());

        Assert.Equal(S.ProteinBehind, insight.Status);
        Assert.Contains("65 / 130 g", insight.Message);
        Assert.Contains("65 g daha", insight.Message);
        Assert.Equal(Q.CompleteProtein, insight.SuggestedQuestion);
    }

    [Fact]
    public void OnlyBreakfastLogged_DoesNotJudgeMacrosAsMissing()
    {
        // 400 kcal (%19) kaydedildi: protein mutlak olarak cok dusuk ama henuz yenmemis ogunler tuketilmedi sayilmaz.
        var insight = NutritionInsightEvaluator.Evaluate(Day(400, 10, 60, 12));

        Assert.Equal(S.InProgress, insight.Status);
        Assert.DoesNotContain("gerisinde", insight.Message);
    }

    [Fact]
    public void ProteinProportionalToCalories_IsNotFlagged()
    {
        // %50 kalori, %46 protein: kaydedilen yemege oranla geride degil.
        var insight = NutritionInsightEvaluator.Evaluate(Day(1050, 60, 115, 35));

        Assert.NotEqual(S.ProteinBehind, insight.Status);
        Assert.Equal(S.CarbsOnTrack, insight.Status);
    }

    [Fact]
    public void AllTargetsMet_ReturnsGoalsCompleted()
    {
        var insight = NutritionInsightEvaluator.Evaluate(Day(2050, 128, 220, 68));

        Assert.Equal(S.GoalsCompleted, insight.Status);
        Assert.Contains("tamamladın", insight.Message);
    }

    [Fact]
    public void CaloriesOverTarget_ReturnsExceeded()
    {
        var insight = NutritionInsightEvaluator.Evaluate(Day(2500, 140, 280, 90));

        Assert.Equal(S.CaloriesExceeded, insight.Status);
        Assert.Contains("400 kcal aştın", insight.Message);
    }

    [Fact]
    public void MostCaloriesDone_ReportsRemainingCalories()
    {
        var insight = NutritionInsightEvaluator.Evaluate(Day(1700, 105, 190, 55));

        Assert.Equal(S.CaloriesMostlyDone, insight.Status);
        Assert.Contains("400 kcal kaldı", insight.Message);
    }

    [Fact]
    public void HighFat_IsFlagged()
    {
        var insight = NutritionInsightEvaluator.Evaluate(Day(1400, 85, 120, 85));

        Assert.Equal(S.FatsHigh, insight.Status);
    }

    [Fact]
    public void MissingTargets_DoesNotInventEvaluation()
    {
        var day = Day(500, 20, 50, 10);
        day.DailyCalories = 0;

        Assert.Equal(S.NoTargets, NutritionInsightEvaluator.Evaluate(day).Status);
    }

    [Fact]
    public void Suggestions_AreDynamic_ForEveningAndWorkout()
    {
        var day = SpecExample();

        var evening = NutritionInsightEvaluator.SuggestQuestions(day, day.Insight, localHour: 19, workedOutToday: true);
        Assert.Equal(Q.CompleteProtein, evening[0]);
        Assert.Contains(Q.PostWorkout, evening);
        Assert.Contains(Q.Dinner, evening);

        var morning = NutritionInsightEvaluator.SuggestQuestions(day, day.Insight, localHour: 9, workedOutToday: false);
        Assert.DoesNotContain(Q.Dinner, morning);

        var pastDay = NutritionInsightEvaluator.SuggestQuestions(day, day.Insight, localHour: null, workedOutToday: false);
        Assert.DoesNotContain(Q.Dinner, pastDay);
        Assert.True(pastDay.Count <= 4);
    }

    [Fact]
    public void Suggestions_SkipAlreadyAskedQuestions()
    {
        var day = SpecExample();

        var result = NutritionInsightEvaluator.SuggestQuestions(day, day.Insight, 19, false, new[] { Q.CompleteProtein });

        Assert.DoesNotContain(Q.CompleteProtein, result);
        Assert.Equal(result.Count, result.Distinct().Count());
    }
}
